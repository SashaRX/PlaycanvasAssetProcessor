namespace TexTool.Resources {
    public class TextureResource : BaseResource {
        private int[] resolution = new int[2];
        private int[] resizeResolution = new int[2];

        public int[] Resolution {
            get => resolution;
            set {
                resolution = value;
                OnPropertyChanged(nameof(Resolution));
                OnPropertyChanged(nameof(ResolutionArea));
            }
        }

        public int[] ResizeResolution {
            get => resizeResolution;
            set {
                resizeResolution = value;
                OnPropertyChanged(nameof(ResizeResolution));
                OnPropertyChanged(nameof(ResizeResolutionArea));
            }
        }

        public int ResolutionArea => Resolution[0] * Resolution[1];
        public int ResizeResolutionArea => ResizeResolution[0] * ResizeResolution[1];
    }
}
