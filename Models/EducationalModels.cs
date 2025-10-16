namespace AI_driven_teaching_platform.Models
{
    public class QuestionRequest
    {
        public string Question { get; set; } = string.Empty;
    }

    public class SearchRequest
    {
        public string Query { get; set; } = string.Empty;
    }

    public class EmbeddingRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class ContentRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Difficulty { get; set; } = "Beginner";
    }

    public class DatasetLoadRequest
    {
        public string Source { get; set; } = "wikipedia"; // wikipedia, json, ai-generated, comprehensive
        public string FilePath { get; set; } = "";
    }

    // For JSON dataset loading
    public class EducationalDataset
    {
        public List<Subject> Subjects { get; set; } = new();
    }

    public class Subject
    {
        public string Name { get; set; } = string.Empty;
        public List<Topic> Topics { get; set; } = new();
    }

    public class Topic
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
    }
}
