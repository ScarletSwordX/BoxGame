using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class BounceApexShaderTests
    {
        [Test]
        public void EffectShaderIsPackagedSupportedAndHasNoCompilerErrors()
        {
            var shader = Resources.Load<Shader>("RuleWorkshop/BounceApexFeedback");
            Assert.IsNotNull(shader, "Resources 引用确保玩家构建包含此后处理。");
            Assert.IsTrue(shader.isSupported);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
            var material = new Material(shader);
            try
            {
                Assert.IsTrue(material.HasProperty("_Strength"));
                Assert.IsTrue(material.HasProperty("_Darkness"));
                Assert.IsTrue(material.HasProperty("_DispersionPixels"));
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
