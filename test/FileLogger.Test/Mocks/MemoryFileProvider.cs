using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Karambolo.Extensions.Logging.File.Test.Mocks;

internal sealed class MemoryFileProvider : IFileProvider
{
    private sealed class File
    {
        public sealed class Stream : MemoryStream
        {
            private readonly MemoryFileProvider _fileProvider;
            private readonly File _file;

            public Stream(MemoryFileProvider fileProvider, File file)
            {
                _fileProvider = fileProvider;
                _file = file;
            }

#pragma warning disable CA2215 // Dispose methods should call base class dispose
            protected override void Dispose(bool disposing)
#pragma warning restore CA2215 // Dispose methods should call base class dispose
            {
                _fileProvider.ReleaseFile(_file);
            }
        }

        public bool IsDirectory { get; set; }
        /// <remarks>
        /// Property is initialized after instantiation.
        /// </remarks>
        public Stream Content { get => field!; set; }
        public CancellationTokenSource? ChangeTokenSource { get; set; }
        public bool IsOpen { get; set; }
    }

    internal static string NormalizePath(string path)
    {
        path = Regex.Replace(path, @"/|\\", Path.DirectorySeparatorChar.ToString(), RegexOptions.CultureInvariant);
        if (path.Length > 0 && path[0] == Path.DirectorySeparatorChar)
            path.Substring(1);
        return path;
    }

    private readonly Dictionary<string, File> _catalog = new()
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
            return _catalog.TryGetValue(path, out File? file) ? file.IsDirectory : false;
    }

    public long GetLength(string path)
    {
        path = NormalizePath(path);
        lock (_catalog)
        {
            if (!_catalog.TryGetValue(path, out File? file) || file.IsDirectory)
                return -1;

            return file.Content.Length;
        }
    }

    private void CheckPath(string normalizedPath)
    {
        if (Exists(normalizedPath))
            throw new InvalidOperationException($"A {(IsDirectory(normalizedPath) ? "directory" : "name")} with the same name already exists.");

        string dir = Path.GetDirectoryName(normalizedPath)!;
        if (!Exists(dir))
            throw new InvalidOperationException("Parent directory does not exist.");

        if (!IsDirectory(dir))
            throw new InvalidOperationException("Parent directory is a file.");
    }

    private void CreateDirCore(string normalizedPath)
    {
        if (Exists(normalizedPath))
        {
            if (!IsDirectory(normalizedPath))
                throw new InvalidOperationException("Path (or part of a path) is a file.");

            return;
        }

        string parentDir = Path.GetDirectoryName(normalizedPath)!;

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

    public void CreateFile(string path, string? content = null, Encoding? encoding = null)
    {
        path = NormalizePath(path);
        lock (_catalog)
        {
            CheckPath(path);

            var file = new File();
            file.Content = new File.Stream(this, file);

            if (content is not null)
            {
                var writer = new StreamWriter(file.Content, encoding ?? Encoding.UTF8);
                writer.Write(content);
                writer.Flush();
            }

            _catalog.Add(path, file);
        }
    }

    private File.Stream GetStreamCore(string path, out File file)
    {
        if (!_catalog.TryGetValue(path, out file!) || file.IsDirectory)
            throw new InvalidOperationException("File does not exist.");

        if (file.IsOpen)
            throw new InvalidOperationException("File is in use currently.");

        file.IsOpen = true;

        file.Content.Seek(0, SeekOrigin.Begin);
        return file.Content;
    }

    public MemoryStream GetStream(string path)
    {
        path = NormalizePath(path);
        lock (_catalog)
            return GetStreamCore(path, out _);
    }

    private void ReleaseFile(File file)
    {
        lock (_catalog)
            file.IsOpen = false;
    }

    public string ReadContent(string path)
    {
        using (MemoryStream stream = GetStream(path))
            return new StreamReader(stream).ReadToEnd();
    }

    public void WriteContent(string path, string content, Encoding? encoding = null, bool append = false)
    {
        path = NormalizePath(path);

        CancellationTokenSource? changeTokenSource = null;
        lock (_catalog)
        {
            using (MemoryStream stream = GetStreamCore(path, out File file))
            {
                if (content.Length == 0)
                    return;

                if (!append)
                    stream.SetLength(0);
                else
                    stream.Seek(0, SeekOrigin.End);

                var writer = new StreamWriter(stream, encoding ?? Encoding.UTF8);
                writer.Write(content);
                writer.Flush();

                if (file.ChangeTokenSource is not null)
                {
                    changeTokenSource = file.ChangeTokenSource;
                    file.ChangeTokenSource = new CancellationTokenSource();
                }
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
#pragma warning disable CA1847 // Use char literal for a single character lookup
        if (filter.Contains("*", StringComparison.Ordinal))
            throw new NotImplementedException();
#pragma warning restore CA1847 // Use char literal for a single character lookup

        lock (_catalog)
        {
            if (!_catalog.TryGetValue(filter, out File? file))
                return new CancellationChangeToken(CancellationToken.None);

            file.ChangeTokenSource ??= new CancellationTokenSource();

            return new CancellationChangeToken(file.ChangeTokenSource.Token);
        }
    }
}
