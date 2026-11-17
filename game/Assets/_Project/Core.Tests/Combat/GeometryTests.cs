using NUnit.Framework;
using TheArtifacts.Core.Combat;

namespace TheArtifacts.Core.Tests.Combat
{
    public class GeometryTests
    {
        [Test]
        public void Cone_HitsFrontOnly()
        {
            var cone = new ConeArea(Vec2.Zero, new Vec2(0, 1), 90f, 6f);

            Assert.That(cone.Contains(new Vec2(0, 5)), Is.True, "straight ahead");
            Assert.That(cone.Contains(new Vec2(2, 3)), Is.True, "inside 45° half-angle");
            Assert.That(cone.Contains(new Vec2(4, 1)), Is.False, "outside half-angle");
            Assert.That(cone.Contains(new Vec2(0, -1)), Is.False, "behind");
            Assert.That(cone.Contains(new Vec2(0, 6.5f)), Is.False, "beyond range");
        }

        [Test]
        public void Ring_IsSafeInside()
        {
            var ring = new RingArea(Vec2.Zero, 4f, 12f);

            Assert.That(ring.Contains(new Vec2(2, 0)), Is.False);
            Assert.That(ring.Contains(new Vec2(6, 0)), Is.True);
            Assert.That(ring.Contains(new Vec2(13, 0)), Is.False);
        }

        [Test]
        public void Line_HasLengthAndWidth()
        {
            var line = new LineArea(Vec2.Zero, new Vec2(1, 0), 15f, 3f);

            Assert.That(line.Contains(new Vec2(10, 1.4f)), Is.True);
            Assert.That(line.Contains(new Vec2(10, 1.6f)), Is.False, "outside half-width");
            Assert.That(line.Contains(new Vec2(-1, 0)), Is.False, "behind origin");
            Assert.That(line.Contains(new Vec2(16, 0)), Is.False, "past the end");
        }

        [Test]
        public void Circle_UsesRadius()
        {
            var circle = new CircleArea(new Vec2(1, 1), 5f);

            Assert.That(circle.Contains(new Vec2(6, 1)), Is.True);
            Assert.That(circle.Contains(new Vec2(6.1f, 1)), Is.False);
        }
    }
}
