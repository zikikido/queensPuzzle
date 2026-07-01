#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Puzzby
{
    /// <summary>
    /// Injects the VIBRATE permission into the generated Android manifest at build time — reliable
    /// even when the package's plugin-manifest doesn't merge on its own.
    /// </summary>
    public class HapticsAndroidBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;

            const string ns = "http://schemas.android.com/apk/res/android";
            const string perm = "android.permission.VIBRATE";

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var root = doc.DocumentElement;
            if (root == null) return;

            foreach (XmlNode node in doc.GetElementsByTagName("uses-permission"))
            {
                var name = node.Attributes?["android:name"];
                if (name != null && name.Value == perm) return;   // already present
            }

            var el = doc.CreateElement("uses-permission");
            var attr = doc.CreateAttribute("android", "name", ns);
            attr.Value = perm;
            el.Attributes.Append(attr);
            root.AppendChild(el);
            doc.Save(manifestPath);
        }
    }
}
#endif
