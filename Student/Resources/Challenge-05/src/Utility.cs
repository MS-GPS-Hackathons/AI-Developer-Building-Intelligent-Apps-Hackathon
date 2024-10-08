﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace AIDevHackathon.ConsoleApp.VectorDB.Recipes
{
    internal static class Utility
    {
        public static List<Recipe> ParseDocuments(string Folderpath)
        {
            List<Recipe> ret = new List<Recipe>();

            Directory.GetFiles(Folderpath).ToList().ForEach(f =>
                {
                    var jsonString= System.IO.File.ReadAllText(f);
                    Recipe recipe = JsonConvert.DeserializeObject<Recipe>(jsonString);
                    recipe.id = recipe.name.ToLower().Replace(" ","");
                    ret.Add(recipe);

                }
            );


            return ret;

        }
    }
}
