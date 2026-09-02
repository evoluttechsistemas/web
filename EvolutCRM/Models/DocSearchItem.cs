namespace EvolutCRM.Models
{
    public class DocSearchItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Preview { get; set; } = "";
        public string Url { get; set; } = "";
        public int CodEmp { get; set; }
    }
}