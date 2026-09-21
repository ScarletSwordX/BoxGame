using NUnit.Framework;
using UnityEngine;

namespace RulePyramid.Tests.PlayMode
{
    public class BootstrapSmokeTests
    {
        [Test]
        public void VisualConfigCanBeCreated()
        {
            var config = ScriptableObject.CreateInstance<RulePyramid.Runtime.VisualConfig>();
            Assert.IsNotNull(config);
            Assert.Greater(config.cellSize, 0f);
        }
    }
}
