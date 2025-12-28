using AssetProcessor.Resources;
using System;
using System.Collections;
using System.ComponentModel;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// High-performance comparer for DataGrid sorting using direct property access instead of reflection.
    /// CustomSort with IComparer is 5-10x faster than SortDescription which uses reflection.
    /// </summary>
    public class ResourceComparer : IComparer {
        private readonly string _propertyName;
        private readonly ListSortDirection _direction;
        private readonly int _directionMultiplier;

        public ResourceComparer(string propertyName, ListSortDirection direction) {
            _propertyName = propertyName;
            _direction = direction;
            _directionMultiplier = direction == ListSortDirection.Ascending ? 1 : -1;
        }

        public int Compare(object? x, object? y) {
            if (x == null && y == null) return 0;
            if (x == null) return -1 * _directionMultiplier;
            if (y == null) return 1 * _directionMultiplier;

            // Direct property access - no reflection
            int result = _propertyName switch {
                // BaseResource properties
                "ID" => CompareInt(GetId(x), GetId(y)),
                "Index" => CompareInt(GetIndex(x), GetIndex(y)),
                "Name" => CompareString(GetName(x), GetName(y)),
                "Size" => CompareInt(GetSize(x), GetSize(y)),
                "Status" => CompareString(GetStatus(x), GetStatus(y)),
                "Extension" => CompareString(GetExtension(x), GetExtension(y)),

                // TextureResource properties
                "ResolutionArea" => CompareInt(GetResolutionArea(x), GetResolutionArea(y)),
                "ResizeResolutionArea" => CompareInt(GetResizeResolutionArea(x), GetResizeResolutionArea(y)),
                "CompressedSize" => CompareLong(GetCompressedSize(x), GetCompressedSize(y)),
                "CompressionFormat" => CompareString(GetCompressionFormat(x), GetCompressionFormat(y)),
                "MipmapCount" => CompareInt(GetMipmapCount(x), GetMipmapCount(y)),
                "PresetName" => CompareString(GetPresetName(x), GetPresetName(y)),
                "TextureType" => CompareString(GetTextureType(x), GetTextureType(y)),
                "GroupName" => CompareString(GetGroupName(x), GetGroupName(y)),

                // ModelResource properties
                "UVChannels" => CompareNullableInt(GetUVChannels(x), GetUVChannels(y)),

                // Default: use reflection as fallback
                _ => CompareByReflection(x, y)
            };

            return result * _directionMultiplier;
        }

        // Inline comparison methods for performance
        private static int CompareInt(int a, int b) => a.CompareTo(b);
        private static int CompareLong(long a, long b) => a.CompareTo(b);
        private static int CompareNullableInt(int? a, int? b) {
            if (!a.HasValue && !b.HasValue) return 0;
            if (!a.HasValue) return -1;
            if (!b.HasValue) return 1;
            return a.Value.CompareTo(b.Value);
        }
        private static int CompareString(string? a, string? b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

        // Direct property accessors - no reflection overhead
        private static int GetId(object obj) => obj is BaseResource r ? r.ID : 0;
        private static int GetIndex(object obj) => obj is BaseResource r ? r.Index : 0;
        private static string? GetName(object obj) => obj is BaseResource r ? r.Name : null;
        private static int GetSize(object obj) => obj is BaseResource r ? r.Size : 0;
        private static string? GetStatus(object obj) => obj is BaseResource r ? r.Status : null;
        private static string? GetExtension(object obj) => obj is BaseResource r ? r.Extension : null;

        private static int GetResolutionArea(object obj) => obj is TextureResource t ? t.ResolutionArea : 0;
        private static int GetResizeResolutionArea(object obj) => obj is TextureResource t ? t.ResizeResolutionArea : 0;
        private static long GetCompressedSize(object obj) => obj is TextureResource t ? t.CompressedSize : 0;
        private static string? GetCompressionFormat(object obj) => obj is TextureResource t ? t.CompressionFormat : null;
        private static int GetMipmapCount(object obj) => obj is TextureResource t ? t.MipmapCount : 0;
        private static string? GetPresetName(object obj) => obj is TextureResource t ? t.PresetName : null;
        private static string? GetTextureType(object obj) => obj is TextureResource t ? t.TextureType : null;
        private static string? GetGroupName(object obj) => obj is TextureResource t ? t.GroupName : null;

        private static int? GetUVChannels(object obj) => obj is ModelResource m ? m.UVChannels : null;

        // Fallback for unknown properties
        private int CompareByReflection(object x, object y) {
            var prop = x.GetType().GetProperty(_propertyName);
            if (prop == null) return 0;

            var valX = prop.GetValue(x);
            var valY = prop.GetValue(y);

            if (valX is IComparable compX && valY != null) {
                return compX.CompareTo(valY);
            }
            return 0;
        }
    }
}
