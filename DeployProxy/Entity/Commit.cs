using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// ReSharper disable InconsistentNaming
namespace DeployProxy.Entity
{
    public class Commit
    {
        public string id { get; set; }
        public string message { get; set; }
        public string title { get; set; }
        public DateTime timestamp { get; set; }
        public string url { get; set; }
        public Author author { get; set; }
        public string[] added { get; set; }
        public string[] modified { get; set; }
        public object[] removed { get; set; }
    }
}
