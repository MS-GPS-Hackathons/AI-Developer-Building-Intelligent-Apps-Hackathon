﻿using AIDevHackathon.ConsoleApp.VectorDB.Recipes;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.Schemas;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Container = Microsoft.Azure.Cosmos.Container;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace AIDevHackathon.ConsoleApp.VectorDB.Recipes.Services;

/// <summary>
/// Service to access Azure Cosmos DB for NoSQL.
/// </summary>
public class CosmosDbService
{
    private readonly Container _container;
    CosmosClient? _client;
    Database? _database;

    /// <summary>
    /// Creates a new instance of the service.
    /// </summary>
    /// <param name="endpoint">Endpoint URI.</param>
    /// <param name="key">Account key.</param>
    /// <param name="databaseName">Name of the database to access.</param>
    /// <param name="containerName">Name of the container to access.</param>
    /// <exception cref="ArgumentNullException">Thrown when endpoint, key, databaseName, or containerName is either null or empty.</exception>
    /// <remarks>
    /// This constructor will validate credentials and create a service client instance.
    /// </remarks>
    public CosmosDbService(string endpoint, string databaseName, string containerName, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(databaseName);
        ArgumentException.ThrowIfNullOrEmpty(containerName);


        CosmosSerializationOptions options = new()
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        };

        // Create a new CosmosClient instance and authenticate with Api Key
        _client = new CosmosClientBuilder(endpoint,new AzureKeyCredential(key))
        //_client = new CosmosClientBuilder(endpoint, new DefaultAzureCredential())
           .WithSerializerOptions(options)
           .Build();

        _database = _client?.GetDatabase(databaseName);
        var container = _database?.GetContainer(containerName);

        _container = container ??
            throw new ArgumentException("Unable to connect to existing Azure Cosmos DB container or database.");
    }

    /// <summary>
    /// Checks if the Cosmos DB container exists.
    /// </summary>
    /// <returns>True if the container exists, otherwise false.</returns>
    public async Task<bool> CheckCollectionExistsAsync()
    {
        try
        {
            // Check if the container exists
            return await _container.ReadContainerAsync() != null;
        }
        catch (Exception ex)
        {
            return false;
        }
    }


    /// <summary>
    /// Creates a new Cosmos DB container with specified properties.
    /// </summary>
    /// <param name="databaseName">The name of the database.</param>
    /// <param name="containerName">The name of the container to create.</param>
    /// <returns>True if the container is created successfully, otherwise false.</returns>
    public async Task<bool> CreateCosmosContainerAsync(string databaseName, string containerName)
    {
        try
        {
            var throughputProperties = ThroughputProperties.CreateAutoscaleThroughput(1000);

            // Define new container properties including the vector indexing policy
            var properties = new ContainerProperties(id: containerName, partitionKeyPath: "/id")
            {
                // Set the default time to live for cache items to 1 day
                DefaultTimeToLive = 86400,

                // Define the vector embedding container policy
                VectorEmbeddingPolicy = new(
                new Collection<Embedding>(
                [
                    new Embedding
                    {
                    Path = "/vectors",
                    DataType = VectorDataType.Float32,
                    DistanceFunction = DistanceFunction.Cosine,
                    Dimensions = 1536
                }
                ])),
                IndexingPolicy = new IndexingPolicy
                {
                    // Define the vector index policy
                    VectorIndexes =
                    [
                        new VectorIndexPath
                        {
                            Path = "/vectors",
                            Type = VectorIndexType.QuantizedFlat
                        }
                    ]
                }
            };

            // Create the container
            Container container = _database.CreateContainerIfNotExistsAsync(properties, throughputProperties).Result;

            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
    }


    /// <summary>
    /// Performs a vector search on the recipes and returns the top 3 results based on similarity score.
    /// </summary>
    /// <param name="vectors">The vector to compare against the recipe vectors.</param>
    /// <param name="similarityScore">The minimum similarity score to filter the results.</param>
    /// <returns>A list of recipes that match the vector search criteria.</returns>
    public async Task<List<Recipe>> SingleVectorSearch(float[] vectors, double similarityScore)
    {
        // Define the query to search for recipes based on the vector similarity score
        var queryText = @"SELECT Top 3 x.name,x.description, x.ingredients, x.cuisine,x.difficulty, x.prepTime,x.cookTime,x.totalTime,x.servings, x.similarityScore
                            FROM (SELECT c.name,c.description, c.ingredients, c.cuisine,c.difficulty, c.prepTime,c.cookTime,c.totalTime,c.servings,
                                VectorDistance(c.vectors, @vectors, false) as similarityScore FROM c) x
                                    WHERE x.similarityScore > @similarityScore ORDER BY x.similarityScore desc";

        // Define the query parameters
        var queryDef = new QueryDefinition(
                query: queryText)
            .WithParameter("@vectors", vectors)
            .WithParameter("@similarityScore", similarityScore);

        // Execute the query and retrieve the results
        using FeedIterator<Recipe> resultSet = _container.GetItemQueryIterator<Recipe>(queryDefinition: queryDef);

        List<Recipe> recipes = new List<Recipe>();

        // Iterate through the results
        while (resultSet.HasMoreResults)
        {
            FeedResponse<Recipe> response = await resultSet.ReadNextAsync();
            recipes.AddRange(response);
        }
        return recipes;
    }

    /// <summary>
    /// Gets a list of all recipes where embeddings are null.
    /// </summary>
    public async Task<List<Recipe>> GetRecipesToVectorizeAsync()
    {
        // Define the query to search for recipes that are not vectorized
        var query = new QueryDefinition("SELECT * FROM c WHERE IS_ARRAY(c.vectors)=false");


        // Execute the query and retrieve the results
        FeedIterator<Recipe> results = _container.GetItemQueryIterator<Recipe>(query);

        List<Recipe> output = new();
        // Iterate through the results
        while (results.HasMoreResults)
        {
            FeedResponse<Recipe> response = await results.ReadNextAsync();
            output.AddRange(response);
        }
        return output;
    }

    /// <summary>
    /// Gets a list of all recipes
    /// </summary>
    public async Task<List<Recipe>> GetRecipesAsync()
    {
        // Define the query to search for all recipes
        var query = new QueryDefinition("SELECT * FROM c");

        // Execute the query and retrieve the results
        FeedIterator<Recipe> results = _container.GetItemQueryIterator<Recipe>(query);

        List<Recipe> output = new();
        // Iterate through the results
        while (results.HasMoreResults)
        {
            FeedResponse<Recipe> response = await results.ReadNextAsync();
            output.AddRange(response);
        }
        return output;
    }

    /// <summary>
    /// Gets a count of all recipes based on embeddings status.
    /// </summary>
    public async Task<int> GetRecipeCountAsync(bool withEmbedding)
    {
        // Define the query to get Recipes count based on embeddings status
        var query = new QueryDefinition("SELECT value Count(c.id) FROM c WHERE IS_ARRAY(c.vectors)=@status")
            .WithParameter("@status", withEmbedding);

        // Execute the query and retrieve the results
        var queryResultSetIterator = _container.GetItemQueryIterator<int>(query);
        var queryResultSet = await queryResultSetIterator.ReadNextAsync();

        // Retrieve the count value from the results
        return queryResultSet.FirstOrDefault();
    }

    /// <summary>
    /// Adds a list of recipes to the Cosmos DB container in bulk.
    /// </summary>
    /// <param name="recipes">The list of recipes to add.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddRecipesAsync(List<Recipe> recipes)
    {
        // Create a new BulkOperations instance to add recipes in bulk
        BulkOperations<Recipe> bulkOperations = new BulkOperations<Recipe>(recipes.Count);
        foreach (var recipe in recipes)
        {
            bulkOperations.Tasks.Add(CaptureOperationResponse(_container.CreateItemAsync(recipe, new PartitionKey(recipe.id)), recipe));
        }
        BulkOperationResponse<Recipe> bulkOperationResponse = await bulkOperations.ExecuteAsync();
    }


    /// <summary>
    /// Updates the vectors of recipes in the Cosmos DB container in bulk.
    /// </summary>
    /// <param name="dictInput">A dictionary where the key is the recipe ID and the value is the vector to update.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateRecipesAsync(Dictionary<string, float[]> dictInput)
    {
        // Create a new BulkOperations instance to update recipes in bulk
        BulkOperations<Recipe> bulkOperations = new BulkOperations<Recipe>(dictInput.Count);
        foreach (KeyValuePair<string, float[]> entry in dictInput)
        {
            await _container.PatchItemAsync<Recipe>(entry.Key, new PartitionKey(entry.Key), patchOperations: new[] { PatchOperation.Add("/vectors", entry.Value) });
        }
    }

    /// <summary>
    /// Class to handle bulk operations for a generic type T.
    /// </summary>
    /// <typeparam name="T">The type of the items being operated on.</typeparam>
    private class BulkOperations<T>(int operationCount)
    {
        public readonly List<Task<OperationResponse<T>>> Tasks = new(operationCount);

        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        /// <summary>
        /// Executes all the bulk operations and returns a summary of the results.
        /// </summary>
        /// <returns>A <see cref="BulkOperationResponse{T}"/> containing the results of the bulk operations.</returns>
    
        public async Task<BulkOperationResponse<T>> ExecuteAsync()
        {
            await Task.WhenAll(this.Tasks);
            this.stopwatch.Stop();
            return new BulkOperationResponse<T>
            {
                TotalTimeTaken = this.stopwatch.Elapsed,
                TotalRequestUnitsConsumed = this.Tasks.Sum(task => task.Result.RequestUnitsConsumed),
                SuccessfulDocuments = this.Tasks.Count(task => task.Result.IsSuccessful),
                Failures = this.Tasks.Where(task => !task.Result.IsSuccessful).Select(task => (task.Result.Item, task.Result.CosmosException)).ToList()
            };
        }
    }

 /// <summary>
    /// Represents the summary of the results of the bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of the items being operated on.</typeparam>
  
    public class BulkOperationResponse<T>
    {
        public TimeSpan TotalTimeTaken { get; set; }
        public int SuccessfulDocuments { get; set; } = 0;
        public double TotalRequestUnitsConsumed { get; set; } = 0;

        public IReadOnlyList<(T, Exception)>? Failures { get; set; }
    }

/// <summary>
    /// Represents the response of a single operation in the bulk operations.
    /// </summary>
    /// <typeparam name="T">The type of the item being operated on.</typeparam>
  
    public class OperationResponse<T>
    {
        public T? Item { get; set; }
        public double RequestUnitsConsumed { get; set; } = 0;
        public bool IsSuccessful { get; set; }
        public Exception? CosmosException { get; set; }
    }

    /// <summary>
    /// Captures the response of an operation and returns an <see cref="OperationResponse{T}"/> object.
    /// </summary>
    /// <typeparam name="T">The type of the item being operated on.</typeparam>
    /// <param name="task">The task representing the operation.</param>
    /// <param name="item">The item being operated on.</param>
    /// <returns>An <see cref="OperationResponse{T}"/> object containing the result of the operation.</returns>

    private static async Task<OperationResponse<T>> CaptureOperationResponse<T>(Task<ItemResponse<T>> task, T item)
    {
        try
        {
            ItemResponse<T> response = await task;
            return new OperationResponse<T>
            {
                Item = item,
                IsSuccessful = true,
                RequestUnitsConsumed = task.Result.RequestCharge
            };
        }
        catch (Exception ex)
        {
            if (ex is CosmosException cosmosException)
            {
                return new OperationResponse<T>
                {
                    Item = item,
                    RequestUnitsConsumed = cosmosException.RequestCharge,
                    IsSuccessful = false,
                    CosmosException = cosmosException
                };
            }

            return new OperationResponse<T>
            {
                Item = item,
                IsSuccessful = false,
                CosmosException = ex
            };
        }
    }


}