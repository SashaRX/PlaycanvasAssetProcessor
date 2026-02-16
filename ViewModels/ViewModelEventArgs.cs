using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using System;
using System.Collections.Generic;

namespace AssetProcessor.ViewModels {
    public sealed class TextureProcessingCompletedEventArgs : EventArgs {
        public TextureProcessingCompletedEventArgs(TextureProcessingResult result) {
            Result = result;
        }

        public TextureProcessingResult Result { get; }
    }

    public sealed class TexturePreviewLoadedEventArgs : EventArgs {
        public TexturePreviewLoadedEventArgs(TextureResource texture, TexturePreviewResult preview) {
            Texture = texture;
            Preview = preview;
        }

        public TextureResource Texture { get; }

        public TexturePreviewResult Preview { get; }
    }

    public sealed class ProjectSelectionChangedEventArgs : EventArgs {
        public ProjectSelectionChangedEventArgs(KeyValuePair<string, string> project) {
            SelectedProject = project;
        }

        public KeyValuePair<string, string> SelectedProject { get; }
    }

    public sealed class BranchSelectionChangedEventArgs : EventArgs {
        public BranchSelectionChangedEventArgs(Branch branch) {
            SelectedBranch = branch;
        }

        public Branch SelectedBranch { get; }
    }
}
