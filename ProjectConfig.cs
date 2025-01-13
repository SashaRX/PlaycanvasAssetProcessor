using Newtonsoft.Json;
using System.Configuration;
using System.IO;

namespace AssetProcessor {
    public class ProjectConfig {
        public string? ProjectId { get; set; }
        public string? BranchId { get; set; }
        public string? PlaycanvasApiKey { get; set; }
        public string? ProjectsFolderPath { get; set; }

        public static ProjectConfig Load(string filePath) {
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            }

            string json = File.ReadAllText(filePath);
            ProjectConfig config = JsonConvert.DeserializeObject<ProjectConfig>(json) ?? throw new ConfigurationErrorsException("Failed to deserialize configuration.");
            config.Validate();
            return config;
        }

        public void Save(string filePath) {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public void Validate() {
            if (string.IsNullOrEmpty(ProjectId))
                throw new ConfigurationErrorsException("ProjectId is required.");
            if (string.IsNullOrEmpty(BranchId))
                throw new ConfigurationErrorsException("BranchId is required.");
            if (string.IsNullOrEmpty(PlaycanvasApiKey))
                throw new ConfigurationErrorsException("PlaycanvasApiKey is required.");
            if (string.IsNullOrEmpty(ProjectsFolderPath) || !Directory.Exists(ProjectsFolderPath))
                throw new ConfigurationErrorsException("ProjectsFolderPath is invalid.");
        }
    }
}