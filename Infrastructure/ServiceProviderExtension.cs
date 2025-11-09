using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows.Markup;

namespace AssetProcessor.Infrastructure {
    /// <summary>
    /// Маркап-расширение для получения сервисов из DI контейнера.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class ServiceProviderExtension : MarkupExtension {
        /// <summary>
        /// Тип сервиса, который требуется получить.
        /// </summary>
        public Type? ServiceType { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) {
            if (ServiceType is null) {
                throw new InvalidOperationException("ServiceType must be specified for ServiceProviderExtension.");
            }

            if (App.Services is null) {
                throw new InvalidOperationException("Service provider is not initialized.");
            }

            return App.Services.GetRequiredService(ServiceType);
        }
    }
}
