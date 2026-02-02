using AssetProcessor.ModelConversion.Core;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services {
    public interface IModelConversionService {
        Task<ModelConversionServiceResult> ConvertAsync(
            ModelResource model,
            ModelConversionSettings settings,
            CancellationToken cancellationToken = default);
    }
}
