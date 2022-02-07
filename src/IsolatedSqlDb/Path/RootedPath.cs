using System;
using System.IO;

namespace IsolatedSqlDb.Path
{
    public class RootedPath
    {
        private readonly string _path;

        public RootedPath(string path)
        {
            _path = path;
            if (!System.IO.Path.IsPathRooted(path))
            {
                throw new InvalidOperationException($"Path '{path}' is not rooted.");
            }
        }

        public static RootedPath operator +(RootedPath left, RelativePath right)
        {
            return new RootedPath(System.IO.Path.Combine(left.ToString(), right.ToString()));
        }
        public static RootedPath GetTempPath(RelativePath? subPath = null)
        {
            RootedPath path = new RootedPath(System.IO.Path.GetTempPath());

            if (subPath != null)
            {
                path += subPath;
            }

            return path;
        }

        public RootedPath EnsureExists()
        {
            if (!Exists())
            {
                Directory.CreateDirectory(_path);
            }

            return this;
        }

        public bool Exists()
        {
            return Directory.Exists(_path);
        }

        public RootedPath Concat(string right)
        {
            if (System.IO.Path.IsPathRooted(right))
                throw new InvalidOperationException($"Can't concatenate '{this}' with rooted path '{right}'");

            return this + new RelativePath(right);
        }

        public static implicit operator string(RootedPath rootedPath) => rootedPath.ToString();

        public override string ToString() => _path;
        
    }
}