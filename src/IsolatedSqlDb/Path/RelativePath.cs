using System;

namespace IsolatedSqlDb.Path
{

    public class RelativePath
    {
        

        private readonly string _path;

        public RelativePath(string path)
        {
            _path = path;
            if (System.IO.Path.IsPathRooted(path))
            {
                throw new InvalidOperationException($"Path '{path}' is rooted.");
            }
        }
        public RelativePath Concat(string right)
        {
            if (System.IO.Path.IsPathRooted(right))
                throw new InvalidOperationException($"Can't concatenate '{this}' with rooted path '{right}'");

            return this + new RelativePath(right);
        }

        public static RelativePath operator +(RelativePath left, RelativePath right)
        {
            return new RelativePath(System.IO.Path.Combine(left.ToString(), right.ToString()));
        }

        public static implicit operator RelativePath(string value) => new RelativePath(value);

        public RootedPath Full() => new RootedPath(System.IO.Path.GetFullPath(this.ToString()));
        public override string ToString() => _path;

        /// <summary>
        /// Find a path by expression <see cref="partOfPath"/>, starting from <see cref="startSearchingFrom"/>.
        /// 
        /// </summary>
        /// <param name="partOfPath"></param>
        /// <param name="startSearchingFrom">Where to start searching from. If not specified, then use current location</param>
        /// <returns></returns>
        public static RootedPath? Find(RelativePath partOfPath, RootedPath? startSearchingFrom = null)
        {
            if (partOfPath == null)
            {
                throw new ArgumentNullException(nameof(partOfPath));
            }

            startSearchingFrom ??= new RootedPath(AppDomain.CurrentDomain.BaseDirectory
                                                  ?? throw new ArgumentNullException(
                                                      nameof(startSearchingFrom),
                                                      "Appdomain base dir is empty. Specify startSearchingFrom"));
            var relativePath = partOfPath;
            for (int i = 0; i < 10; i++)
            {
                var fullPath = startSearchingFrom + relativePath;
                if (fullPath.Exists())
                {
                    return fullPath;
                }
                relativePath = "../" + relativePath;
            }
            throw new InvalidOperationException($"Failed to find path {partOfPath}\nstartSearchingFrom: {startSearchingFrom}");
        }


    }
}