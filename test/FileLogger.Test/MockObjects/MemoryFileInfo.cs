using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.MockObjects
{
    class MemoryFileInfo : IFileInfo
    {
        readonly MemoryFileProvider _owner;

        public MemoryFileInfo(MemoryFileProvider owner, string path)
        {
            _owner = owner;
            Name = Path.GetFileName(path);
            PhysicalPath = path;
        }

        public bool Exists => _owner.Exists(PhysicalPath);

        public long Length => _owner.GetLength(PhysicalPath);

        public string PhysicalPath { get; }

        public string Name { get; }

        public DateTimeOffset LastModified => throw new NotImplementedException();

        public bool IsDirectory => _owner.IsDirectory(PhysicalPath);

        public Stream CreateReadStream()
        {
            var stream = new MemoryStream();

            Encoding encoding;
            if (Encoding != null)
            {
                encoding = Encoding;
                var preamble = encoding.GetPreamble();

                stream.Write(preamble, 0, preamble.Length);
            }
            else
                encoding = Encoding.UTF8;

            var content = encoding.GetBytes(Content.ToString());
            stream.Write(content, 0, content.Length);

            stream.Position = 0;

            return stream;
        }

        public string Content => _owner.ReadContent(PhysicalPath);

        public Encoding Encoding => _owner.GetEncoding(PhysicalPath);
    }
}
