using AssetProcessor.Infrastructure.Enums;
using System;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IConnectionWorkflowCoordinator {
    Task<ConnectionState> DetermineRefreshStateAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles);
    Task<ConnectionState> DeterminePostDownloadStateAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles);
}
