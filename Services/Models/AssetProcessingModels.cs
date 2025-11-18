namespace AssetProcessor.Services.Models;

using AssetProcessor.Resources;
using System.Collections.Generic;

public enum AssetProcessingResultType {
    Texture,
    Model,
    Material
}

public sealed record AssetProcessingParameters(
    string ProjectsRoot,
    string ProjectName,
    IReadOnlyDictionary<int, string> FolderPaths,
    int AssetIndex);

public sealed record AssetProcessingResult(
    AssetProcessingResultType ResultType,
    BaseResource Resource);
