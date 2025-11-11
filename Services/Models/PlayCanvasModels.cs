using System.Text.Json;

namespace AssetProcessor.Services.Models {
    public sealed class PlayCanvasProjectInfo {
        public PlayCanvasProjectInfo(string id, string name) {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
    }

    public sealed class PlayCanvasBranchInfo {
        public PlayCanvasBranchInfo(string id, string name) {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
    }

    public sealed class PlayCanvasAssetFileInfo {
        public PlayCanvasAssetFileInfo(long? size, string? hash, string? filename, string? url, int? width, int? height) {
            Size = size;
            Hash = hash;
            Filename = filename;
            Url = url;
            Width = width;
            Height = height;
        }

        public long? Size { get; }
        public string? Hash { get; }
        public string? Filename { get; }
        public string? Url { get; }
        public int? Width { get; }
        public int? Height { get; }
    }

    public sealed class PlayCanvasAssetSummary {
        public PlayCanvasAssetSummary(
            int id,
            string type,
            string? name,
            string? path,
            int? parent,
            PlayCanvasAssetFileInfo? file,
            JsonElement raw) {
            Id = id;
            Type = type;
            Name = name;
            Path = path;
            Parent = parent;
            File = file;
            Raw = raw;
        }

        public int Id { get; }
        public string Type { get; }
        public string? Name { get; }
        public string? Path { get; }
        public int? Parent { get; }
        public PlayCanvasAssetFileInfo? File { get; }
        public JsonElement Raw { get; }

        public string ToJsonString() => Raw.GetRawText();
    }

    public sealed class PlayCanvasAssetDetail {
        public PlayCanvasAssetDetail(int id, string type, string? name, JsonElement raw) {
            Id = id;
            Type = type;
            Name = name;
            Raw = raw;
        }

        public int Id { get; }
        public string Type { get; }
        public string? Name { get; }
        public JsonElement Raw { get; }

        public string ToJsonString() => Raw.GetRawText();
    }
}
