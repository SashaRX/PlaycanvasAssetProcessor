using AssetProcessor.Services.Models;
using System;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IConnectionWorkflowCoordinator {
    Task<ConnectionWorkflowResult> EvaluateRefreshAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles);
    Task<ConnectionWorkflowResult> EvaluatePostDownloadAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles);
}
