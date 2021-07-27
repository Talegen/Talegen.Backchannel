namespace Talegen.Backchannel.Models
{
    /// <summary>
    /// This class represents a file item in a form request.
    /// </summary>
    public class FileItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileItem" /> class.
        /// </summary>
        public FileItem()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileItem" /> class.
        /// </summary>
        /// <param name="contents">The file contents.</param>
        public FileItem(byte[] contents)
            : this(contents, null) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileItem" /> class.
        /// </summary>
        /// <param name="contents">The file contents.</param>
        /// <param name="filename">The filename.</param>
        public FileItem(byte[] contents, string filename)
            : this(contents, filename, null) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileItem" /> class.
        /// </summary>
        /// <param name="contents">The file contents.</param>
        /// <param name="filename">The filename.</param>
        /// <param name="contenttype">The contenttype.</param>
        public FileItem(byte[] contents, string filename, string contenttype)
        {
            this.Contents = contents;
            this.FileName = filename;
            this.ContentType = contenttype;
        }

        /// <summary>
        /// Gets or sets the file contents.
        /// </summary>
        /// <value>The file contents.</value>
        public byte[] Contents { get; set; }

        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        /// <value>The name of the file.</value>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the type of the content.
        /// </summary>
        /// <value>The type of the content.</value>
        public string ContentType { get; set; }
    }
}