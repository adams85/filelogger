using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Karambolo.Extensions.Logging.File.Test.Mocks
{
    internal class MemoryFileProvider : IFileProvider
    {
        internal static string NormalizePath(string path)
        {
            path = Regex.Replace(path, @"/|\\", Path.DirectorySeparatorChar.ToString());
            if (path.Length > 0 && path[0] == Path.DirectorySeparatorChar)
                path.Substring(1);
            return path;
        }

        private class File
        {
            public bool IsDirectory { get; set; }
            public StringBuilder Content { get; set; }
            public Encoding Encoding { get; set; }
            public CancellationTokenSource ChangeTokenSource { get; set; }
        }

        private readonly Dictionary<string, File> _catalog = new Dictionary<string, File>
        {
            { string.Empty, new File { IsDirectory = true } }
        };

        public bool Exists(string path)
        {
            path = NormalizePath(path);
            lock (_catalog)
                return _catalog.ContainsKey(path);
        }

        public bool IsDirectory(string path)
        {
            path = NormalizePath(path);
            lock (_catalog)
                return _catalog.TryGetValue(path, out File file) ? file.IsDirectory : false;
        }

        public Encoding GetEncoding(string path)
        {
            path = NormalizePath(path);
            lock (_catalog)
                return _catalog.TryGetValue(path, out File file) && !file.IsDirectory ? file.Encoding : null;
        }

        public int GetLength(string path)
        {
            path = NormalizePath(path);
            lock (_catalog)
                return ReadContent(path)?.Length ?? -1;
        }

        private void CheckPath(string normalizedPath)
        {
            if (Exists(normalizedPath))
                throw new InvalidOperationException($"A {(IsDirectory(normalizedPath) ? "directory" : "name")} with the same name already exists.");

            var dir = Path.GetDirectoryName(normalizedPath);
            if (!Exists(dir))
                throw new InvalidOperationException("Parent directory does not exist.");

            if (!IsDirectory(dir))
                throw new InvalidOperationException("Parent directory is a file.");
        }

        public void OpenFile(string path)
        {
            path = NormalizePath(path);
            lock (_catalog)
            {
                if (!Exists(path))
                    throw new InvalidOperationException("File does not exist.");

                if (IsDirectory(path))
                    throw new InvalidOperationException("Path is a directory.");
            }
        }

        private void CreateDirCore(string normalizedPath)
        {
            if (Exists(normalizedPath))
            {
                if (!IsDirectory(normalizedPath))
                    throw new InvalidOperationException("Path (or part of a path) is a file.");

                return;
            }

            var parentDir = Path.GetDirectoryName(normalizedPath);

            if (!Exists(parentDir))
                CreateDirCore(parentDir);

            _catalog.Add(normalizedPath, new File { IsDirectory = true });
        }

        public void CreateDir(string path)
        {
            path = NormalizePath(path);
            lock (_catalog)
                CreateDirCore(path);
        }

        public void CreateFile(string path, string content = null, Encoding encoding = null)
        {
            path = NormalizePath(path);
            lock (_catalog)
            {
                CheckPath(path);

                _catalog.Add(path, new File { Content = new StringBuilder(content ?? string.Empty), Encoding = encoding });
            }
        }

        public string ReadContent(string path)
        {
            path = NormalizePath(path);
            lock (_catalog)
                return _catalog.TryGetValue(path, out File file) && !file.IsDirectory ? file.Content.ToString() : null;
        }

        public void WriteContent(string path, string content, bool append = false)
        {
            path = NormalizePath(path);

            CancellationTokenSource changeTokenSource = null;
            lock (_catalog)
            {
                if (!_catalog.TryGetValue(path, out File file))
                    throw new InvalidOperationException("File does not exist.");

                if (!append)
                    file.Content.Clear();

                file.Content.Append(content);

                if (file.ChangeTokenSource != null)
                {
                    changeTokenSource = file.ChangeTokenSource;
                    file.ChangeTokenSource = new CancellationTokenSource();
                }
            }

            changeTokenSource?.Cancel();
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            throw new NotImplementedException();
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return new MemoryFileInfo(this, subpath);
        }

        public IChangeToken Watch(string filter)
        {
            if (filter.Contains("*"))
                throw new NotImplementedException();

            lock (_catalog)
            {
                if (!_catalog.TryGetValue(filter, out File file))
                    return new CancellationChangeToken(CancellationToken.None);

                if (file.ChangeTokenSource == null)
                    file.ChangeTokenSource = new CancellationTokenSource();

                return new CancellationChangeToken(file.ChangeTokenSource.Token);
            }
        }
    }
}
