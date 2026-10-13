namespace WindowsUpdateManager.Models
{
    public class UpdateInfo
    {
        public string Title { get; set; }
        public string KBArticleId { get; set; }
        public double SizeMB { get; set; }
        public bool RebootRequired { get; set; }
        public string Category { get; set; }
        public bool Selected { get; set; }

        public UpdateInfo()
        {
            Selected = true;
        }
    }
}
