using NUnit.Framework;
using RulePyramid.Editor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class EditorCellLabelTests
    {
        static Vector2 Measure(string text,int font)=>new Vector2(text.Length*font*.6f,font+2);

        [Test]
        public void FullNameUsesLargestSizeThatFitsBothDimensions()
        {
            var fit=EditorCellLabel.Fit("ROBOT","","",new Vector2(39,16),Measure);
            Assert.AreEqual("ROBOT",fit.Text);
            Assert.AreEqual(13,fit.FontSize);
            Assert.LessOrEqual(Measure(fit.Text,fit.FontSize).x,39);
            Assert.LessOrEqual(Measure(fit.Text,fit.FontSize).y,16);
        }

        [Test]
        public void NarrowCellUsesFirstAndLastLettersInsteadOfLayerArrow()
        {
            var fit=EditorCellLabel.Fit("ROBOT","↑"," ×2",new Vector2(12,20),Measure);
            Assert.AreEqual("RT",fit.Text);
            Assert.AreEqual(10,fit.FontSize);
        }

        [Test]
        public void HeightAlsoTriggersShortFormAndUnreadableCellsRemainEmpty()
        {
            var fit=EditorCellLabel.Fit("CLOUD","","",new Vector2(60,10),Measure);
            Assert.AreEqual("CD",fit.Text);
            Assert.AreEqual(8,fit.FontSize);
            Assert.IsTrue(string.IsNullOrEmpty(EditorCellLabel.Fit("CLOUD","","",new Vector2(4,4),Measure).Text));
            Assert.IsTrue(string.IsNullOrEmpty(EditorCellLabel.Fit("","","",new Vector2(20,20),Measure).Text));
        }

        [TestCase("ROCK","ROC","RK")]
        [TestCase("ROBOT","ROB","RT")]
        [TestCase("CLOUD","CLO","CD")]
        [TestCase("SPRING","SPR","SG")]
        public void NameTransitionsThroughThreeDistinctDisplaySizes(string name,string medium,string small)
        {
            Assert.AreEqual(name,EditorCellLabel.Fit(name,"","",new Vector2(100,24),Measure).Text);
            Assert.AreEqual(medium,EditorCellLabel.Fit(name,"","",new Vector2(20,20),Measure).Text);
            var fit=EditorCellLabel.Fit(name,"","",new Vector2(12,20),Measure);
            Assert.AreEqual(small,fit.Text);
            Assert.LessOrEqual(Measure(fit.Text,fit.FontSize).x,12);
        }

        [Test]
        public void ShortWordsKeepTheirOriginalLetters()
        {
            Assert.AreEqual("IS",EditorCellLabel.Fit("IS","","",new Vector2(12,20),Measure).Text);
            Assert.AreEqual("IS",EditorCellLabel.Fit("IS","↑"," ×2",new Vector2(12,10),Measure).Text);
            Assert.AreEqual("A",EditorCellLabel.Fit("A","","",new Vector2(12,10),Measure).Text);
        }

        [Test]
        public void EnlargingCellRestoresFullName()
        {
            Assert.AreEqual("Tra",EditorCellLabel.Fit("TransparentGlass","","",new Vector2(20,20),Measure).Text);
            Assert.AreEqual("TransparentGlass",EditorCellLabel.Fit("TransparentGlass","","",new Vector2(200,28),Measure).Text);
        }
    }
}
