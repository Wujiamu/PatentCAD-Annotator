using System.Collections.Generic;
using Newtonsoft.Json;

namespace PatentOffice.Visio
{
    internal sealed class PatentDictionary
    {
        [JsonProperty("metadata")]
        public DictionaryMetadata Metadata { get; set; }

        [JsonProperty("entries")]
        public List<PatentEntry> Entries { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }

        public PatentDictionary()
        {
            Metadata = new DictionaryMetadata();
            Entries = new List<PatentEntry>();
            Warnings = new List<string>();
        }
    }

    internal sealed class DictionaryMetadata
    {
        [JsonProperty("source_file")]
        public string SourceFile { get; set; }

        [JsonProperty("extracted_at")]
        public string ExtractedAt { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    internal sealed class PatentEntry
    {
        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("occurrences")]
        public int Occurrences { get; set; }
    }
}
