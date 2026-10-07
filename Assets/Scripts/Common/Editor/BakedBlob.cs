using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// One binary the server packs and the build ships: where it lives, and how to go and get it.
    ///
    /// The game bakes more than one of these now (winstats, rivals), and they are identical in
    /// everything except the fetch — same Resources folder, same compare-with-baked, same "commit
    /// it or the build ships what git hasn't got". So the exporters describe their blob with this
    /// and <see cref="BakedBlobsBuildCheck"/> handles the rest, once, for all of them.
    ///
    /// Fetch is a delegate rather than an interface because the differences are entirely inside
    /// it: a URL, a request shape, and a validator that is the game's own reader.
    /// </summary>
    public sealed class BakedBlob
    {
        /// <summary>What to call it in a dialog — "WinStats", "Rivals".</summary>
        public readonly string Name;

        /// <summary>Where the build reads it from.</summary>
        public readonly string Path;

        readonly Func<byte[]> _fetch;

        public BakedBlob(string name, string path, Func<byte[]> fetch)
        {
            Name = name;
            Path = path;
            _fetch = fetch;
        }

        /// <summary>Download and validate. Throws with something a human can act on.</summary>
        public byte[] Fetch() => _fetch();

        /// <summary>What is baked today, or null if nothing is.</summary>
        public byte[] Baked() => File.Exists(Path) ? File.ReadAllBytes(Path) : null;

        public void Save(byte[] bytes)
        {
            File.WriteAllBytes(Path, bytes);
            AssetDatabase.ImportAsset(Path);
        }

        /// <summary>
        /// Stage and commit just this blob. A build must never ship bytes the repo lacks, so the
        /// pre-build "Use Fresh" choice saves AND commits; pushing stays manual. A git failure
        /// logs loudly and does not cancel a build the user already approved.
        /// </summary>
        public void Commit()
        {
            try
            {
                RunGit($"add -- \"{Path}\" \"{Path}.meta\"");
                RunGit($"commit -m \"{Name.ToLowerInvariant()}: refresh baked blob (pre-build)\" -- \"{Path}\" \"{Path}.meta\"");
                Debug.Log($"[{Name}] fresh blob committed to git");
            }
            catch (Exception e)
            {
                Debug.LogError($"[{Name}] blob saved but NOT committed to git — commit it manually! " + e.Message);
            }
        }

        static void RunGit(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),   // the project root = repo root
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                string stderr = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
                if (p.ExitCode != 0) throw new Exception($"git {args} -> {stderr.Trim()}");
            }
        }
    }
}
