﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using SK.NLtoSQL.Models;
using SK.NLtoSQL.Plugins.SQLSchema;
using SK.NLtoSQL.Services;
using System.ComponentModel;
using System.Net;

namespace SK.NLtoSQL
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {               

                //Initialize confguration from appsettings.json
                var azureConfig = new AzureConfiguration();

                //Create the kernel builder and add the Azure OpenAI chat completion plugin
                var builder = Kernel.CreateBuilder() 
                                    .AddAzureOpenAIChatCompletion(azureConfig.AOAIDeploymentId, azureConfig.AOAIEndpoint, azureConfig.AOAIKey);

                //Initialize the database service
                var dbService = new DatabaseService(azureConfig.SQLHostname, azureConfig.SQLUsername, azureConfig.SQLPassword, azureConfig.SQLDatabase);

                //Add the SQLSchemaInfo plugin
                builder.Services.AddSingleton<DatabaseService>(dbService);
                builder.Plugins.AddFromType<SQLSchemaInfo>();
                var kernel = builder.Build();

                // Retrieve the chat completion service from the kernel
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                // Create the chat history
                var systemPrompt = @"
                        You are an sql query assistant, you transform natural language to sql statements. Please explain briefly to the user how you create the sql statements and then show the sql statement to the user.
                        After that please execute the command with sample data and show the result to the user with max of 6 columns.
                        If the user doesn't provide enough information for you to complete the query, you will keep asking questions until you have enough information to complete.
                        ";

                var chatMessages = new ChatHistory(systemPrompt);

                // Start the conversation
                while (true)
                {
                    try
                    {
                        if (chatMessages != null)
                        {
                            // Remove the tool messages from the chat history
                            chatMessages = new ChatHistory(chatMessages.Where(t=>t.Role!= AuthorRole.Tool && t.Metadata==null).ToList());
                          
                        }                        

                        // Get user input
                        System.Console.Write("User > ");
                        chatMessages.AddUserMessage(Console.ReadLine()!);

                        // Get the chat completions
                        OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
                        {                            
                            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                        };
                        var result = chatCompletionService.GetStreamingChatMessageContentsAsync(
                            chatMessages,
                            executionSettings: openAIPromptExecutionSettings,
                            kernel: kernel);

                        // Stream the results
                        var fullMessage = "";
                        await foreach (var content in result)
                        {
                            if (content.Role.HasValue )
                            {
                                System.Console.Write("Assistant > ");
                            }
                            System.Console.Write(content.Content);
                            fullMessage += content.Content;
                        }
                        System.Console.WriteLine();

                        // Add the message from the agent to the chat history
                        chatMessages.AddAssistantMessage(fullMessage);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}