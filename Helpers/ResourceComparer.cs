using AssetProcessor.Resources;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// High-performance comparer for DataGrid sorting using direct property access.
    /// Uses delegate caching to avoid repeated switch evaluation.
    /// </summary>
    public class ResourceComparer : IComparer {
        private readonly Func<object, object, int> _compareFunc;
        private readonly int _directionMultiplier;

        // Static dictionary of comparison functions - initialized once
        private static readonly Dictionary<string, Func<object, object, int>> CompareFuncs = new(StringComparer.Ordinal) {
            // BaseResource properties
            ["ID"] = (x, y) => CompareInt(AsBase(x)?.ID ?? 0, AsBase(y)?.ID ?? 0),
            ["Index"] = (x, y) => CompareInt(AsBase(x)?.Index ?? 0, AsBase(y)?.Index ?? 0),
            ["Name"] = (x, y) => CompareString(AsBase(x)?.Name, AsBase(y)?.Name),
            ["Size"] = (x, y) => CompareInt(AsBase(x)?.Size ?? 0, AsBase(y)?.Size ?? 0),
            ["Status"] = (x, y) => CompareString(AsBase(x)?.Status, AsBase(y)?.Status),
            ["Extension"] = (x, y) => CompareString(AsBase(x)?.Extension, AsBase(y)?.Extension),

            // TextureResource properties
            ["ResolutionArea"] = (x, y) => CompareNullableInt(AsTexture(x)?.ResolutionArea, AsTexture(y)?.ResolutionArea),
            ["ResizeResolutionArea"] = (x, y) => CompareNullableInt(AsTexture(x)?.ResizeResolutionArea, AsTexture(y)?.ResizeResolutionArea),
            ["CompressedSize"] = (x, y) => CompareLong(AsTexture(x)?.CompressedSize ?? 0, AsTexture(y)?.CompressedSize ?? 0),
            ["CompressionFormat"] = (x, y) => CompareString(AsTexture(x)?.CompressionFormat, AsTexture(y)?.CompressionFormat),
            ["MipmapCount"] = (x, y) => CompareInt(AsTexture(x)?.MipmapCount ?? 0, AsTexture(y)?.MipmapCount ?? 0),
            ["PresetName"] = (x, y) => CompareString(AsTexture(x)?.PresetName, AsTexture(y)?.PresetName),
            ["TextureType"] = (x, y) => CompareString(AsTexture(x)?.TextureType, AsTexture(y)?.TextureType),
            ["GroupName"] = (x, y) => CompareString(AsTexture(x)?.GroupName, AsTexture(y)?.GroupName),

            // ModelResource properties
            ["UVChannels"] = (x, y) => CompareNullableInt(AsModel(x)?.UVChannels, AsModel(y)?.UVChannels),
        };

        public ResourceComparer(string propertyName, ListSortDirection direction) {
            _directionMultiplier = direction == ListSortDirection.Ascending ? 1 : -1;

            // Get cached comparison function or create reflection-based fallback
            if (!CompareFuncs.TryGetValue(propertyName, out _compareFunc!)) {
                _compareFunc = CreateReflectionComparer(propertyName);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(object? x, object? y) {
            if (x == null && y == null) return 0;
            if (x == null) return -_directionMultiplier;
            if (y == null) return _directionMultiplier;

            return _compareFunc(x, y) * _directionMultiplier;
        }

        // Inline type casts
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BaseResource? AsBase(object obj) => obj as BaseResource;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TextureResource? AsTexture(object obj) => obj as TextureResource;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ModelResource? AsModel(object obj) => obj as ModelResource;

        // Inline comparison methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareInt(int a, int b) => a.CompareTo(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareLong(long a, long b) => a.CompareTo(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareNullableInt(int? a, int? b) {
            if (!a.HasValue && !b.HasValue) return 0;
            if (!a.HasValue) return -1;
            if (!b.HasValue) return 1;
            return a.Value.CompareTo(b.Value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareString(string? a, string? b) =>
            string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Creates reflection-based comparer for unknown properties.
        /// </summary>
        private static Func<object, object, int> CreateReflectionComparer(string propertyName) {
            return (x, y) => {
                var prop = x.GetType().GetProperty(propertyName);
                if (prop == null) return 0;

                var valX = prop.GetValue(x);
                var valY = prop.GetValue(y);

                if (valX is IComparable compX && valY != null) {
                    return compX.CompareTo(valY);
                }
                return 0;
            };
        }
    }
}
