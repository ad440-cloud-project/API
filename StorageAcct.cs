using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Functions
{
    [JsonObject]
    public class StorageAcct
    {
        [JsonProperty(PropertyName = "STORAGE_URI")]
        public String STORAGE_URI { get; set; }

        [JsonProperty(PropertyName = "STORAGE_KEY")]
        public String STORAGE_KEY { get; set; }

        public override string ToString()
        {
           return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

    }
}