namespace TexTool.Resources {
    public class MaterialResource : BaseResource {
        public string? Shader { get; set; }
        public List<int> TextureIds { get; set; } = [];
        public string? MaterialJsonUrl { get; set; }
        public string? MaterialJsonPath { get; set; }
    }
}
