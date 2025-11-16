using AssetProcessor.Services;
using System.Collections.Generic;

namespace AssetProcessor.Services.Models;

public sealed class BranchSelectionResult {
    public BranchSelectionResult(List<Branch> branches, string? selectedBranchId) {
        Branches = branches;
        SelectedBranchId = selectedBranchId;
    }

    public List<Branch> Branches { get; }
    public string? SelectedBranchId { get; }
}
