using Redmine.Net.Api.Types;

namespace Redmine.Client
{
    /// <summary>
    /// A file the user picked to attach to a *new* issue but that has not been uploaded yet.
    /// The modern API's <see cref="Attachment"/> is immutable (no public constructor, read-only
    /// ContentUrl/ContentType/FileSize/Author), so it can no longer double as the mutable holder
    /// the client used to stash a local file path in. This client-side type carries everything the
    /// attachment grid displays plus the local path needed to read and upload the file on save.
    /// </summary>
    public class PendingAttachment
    {
        /// <summary>Local filesystem path of the file to upload (shown/opened like ContentUrl).</summary>
        public string ContentUrl { get; set; }
        public string Description { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public int FileSize { get; set; }
        public IdentifiableName Author { get; set; }
    }
}
