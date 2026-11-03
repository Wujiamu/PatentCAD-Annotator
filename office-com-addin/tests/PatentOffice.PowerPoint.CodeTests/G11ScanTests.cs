using System;
using System.Collections.Generic;
using System.Linq;
using PatentOffice.PowerPoint;
using Xunit;

namespace PatentOffice.PowerPoint.CodeTests
{
    /// <summary>
    /// G11 / R-012 and R-014. Uses in-memory shape stubs, not a real
    /// presentation: real grouping, text edits and save/reopen still need the
    /// installed host.
    /// </summary>
    public sealed class G11ScanTests
    {
        // ---- R-012: nested groups -------------------------------------------

        [Fact]
        public void G11_TopLevelAnnotation_IsMarked()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            slide.Shapes.Add(Annotation("301", "301"));

            Assert.Contains("301", PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        [Fact]
        public void G11_NestedAnnotation_IsStillMarked()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            FakeShape annotation = Annotation("301", "301");
            slide.Shapes.Add(Group(Group(annotation)));

            Assert.Contains("301", PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        [Fact]
        public void G11_PlainUserGroup_IsNotClaimed()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            FakeShape group = Group(Shape(9, "line"), Shape(17, "just a label"));
            slide.Shapes.Add(group);

            Assert.Empty(PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        [Fact]
        public void G11_SameNumberSeveralTimes_IsLegal()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            slide.Shapes.Add(Annotation("301", "301"));
            slide.Shapes.Add(Annotation("301", "301"));

            Assert.Single(PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        [Fact]
        public void G11_DamagedAnnotation_DoesNotThrow()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            FakeShape broken = Annotation("301", "301");
            broken.ThrowOnGroupItems = true;
            slide.Shapes.Add(broken);

            Assert.Empty(PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        // ---- R-014: visible text vs identity tag ----------------------------

        [Fact]
        public void G11_ChangedLabelText_IsNotReportedAsTheOldNumber()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            slide.Shapes.Add(Annotation("301", "999"));

            Assert.Empty(PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        [Fact]
        public void G11_EmptyLabelText_IsNotReported()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            slide.Shapes.Add(Annotation("301", ""));

            Assert.Empty(PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        [Fact]
        public void G11_LabelTextIsComparedByNumberIdentity()
        {
            FakePresentation pres = new FakePresentation();
            FakeSlide slide = pres.AddSlide();
            slide.Shapes.Add(Annotation("1342A", " 1342a "));

            Assert.Contains("1342A", PowerPointAnnotations.GetMarkedNumbers(pres));
        }

        // ---- R-031: rotated line endpoints ----------------------------------

        [Fact]
        public void G11_RotatedLine_EndpointsFollowTheRotation()
        {
            // Measured on PowerPoint 16.0: a horizontal line drawn left to right
            // at (10,20)-(110,20) rotated 180 degrees puts the end at the left.
            FakeLine line = new FakeLine
            {
                Left = 10, Top = 20, Width = 100, Height = 0, Rotation = 180
            };
            double startX, startY, endX, endY;
            PowerPointAnnotations.GetLineEndpoints(line, out startX, out startY, out endX, out endY);

            Assert.Equal(10.0, endX, 3);
            Assert.Equal(20.0, endY, 3);
            Assert.Equal(110.0, startX, 3);
            Assert.Equal(20.0, startY, 3);
        }

        [Fact]
        public void G11_Rotated90Line_EndpointsFollowClockwiseRotation()
        {
            FakeLine line = new FakeLine
            {
                Left = 10, Top = 20, Width = 100, Height = 0, Rotation = 90
            };
            double startX, startY, endX, endY;
            PowerPointAnnotations.GetLineEndpoints(line, out startX, out startY, out endX, out endY);

            // Centre is (60,20); clockwise 90 degrees sends the end to (60,70).
            Assert.Equal(60.0, endX, 3);
            Assert.Equal(70.0, endY, 3);
            Assert.Equal(60.0, startX, 3);
            Assert.Equal(-30.0, startY, 3);
        }

        [Fact]
        public void G11_UnrotatedLine_IsUnchanged()
        {
            FakeLine line = new FakeLine
            {
                Left = 10, Top = 20, Width = 100, Height = 0, Rotation = 0
            };
            double startX, startY, endX, endY;
            PowerPointAnnotations.GetLineEndpoints(line, out startX, out startY, out endX, out endY);

            Assert.Equal(10.0, startX, 3);
            Assert.Equal(20.0, startY, 3);
            Assert.Equal(110.0, endX, 3);
            Assert.Equal(20.0, endY, 3);
        }

        [Fact]
        public void G11_HorizontalFlipSwapsEndpoints()
        {
            FakeLine line = new FakeLine
            {
                Left = 10, Top = 20, Width = 100, Height = 0, HorizontalFlip = true
            };
            double startX, startY, endX, endY;
            PowerPointAnnotations.GetLineEndpoints(line, out startX, out startY, out endX, out endY);

            Assert.Equal(110.0, startX, 3);
            Assert.Equal(10.0, endX, 3);
        }

        // ---- stubs ----------------------------------------------------------

        private static FakeShape Annotation(string tag, string visibleText)
        {
            FakeShape group = Shape(6, null);
            group.Tags["PATMARKER"] = "PATENTMARKER";
            group.Tags["PATNUMBER"] = tag;
            group.Items.Add(Shape(9, null));
            group.Items.Add(Shape(17, visibleText));
            return group;
        }

        private static FakeShape Group(params FakeShape[] items)
        {
            FakeShape group = Shape(6, null);
            foreach (FakeShape item in items) group.Items.Add(item);
            return group;
        }

        private static FakeShape Shape(int type, string text)
        {
            FakeShape shape = new FakeShape { Type = type };
            if (text != null) shape.TextFrame.TextRange.Text = text;
            return shape;
        }
    }

    public sealed class FakePresentation
    {
        public readonly FakeSlides Slides = new FakeSlides();
        public FakeSlide AddSlide()
        {
            FakeSlide slide = new FakeSlide();
            Slides.Add(slide);
            return slide;
        }
    }

    public sealed class FakeSlides
    {
        private readonly List<FakeSlide> _items = new List<FakeSlide>();
        public int Count { get { return _items.Count; } }
        public void Add(FakeSlide slide) { _items.Add(slide); }
        public FakeSlide Item(int index) { return _items[index - 1]; }
    }

    public sealed class FakeSlide
    {
        public readonly FakeShapes Shapes = new FakeShapes();
    }

    public sealed class FakeShapes
    {
        private readonly List<FakeShape> _items = new List<FakeShape>();
        public int Count { get { return _items.Count; } }
        public void Add(FakeShape shape) { _items.Add(shape); }
        public FakeShape Item(int index) { return _items[index - 1]; }
    }

    public sealed class FakeShape
    {
        public int Type;
        public readonly FakeTags Tags = new FakeTags();
        public readonly FakeShapes Items = new FakeShapes();
        public readonly FakeTextFrame TextFrame = new FakeTextFrame();
        public bool ThrowOnGroupItems;

        public FakeShapes GroupItems
        {
            get
            {
                if (ThrowOnGroupItems) throw new InvalidOperationException("damaged group");
                return Items;
            }
        }

        // Line geometry used by GetLineEndpoints.
        public double Left;
        public double Top;
        public double Width;
        public double Height;
        public double Rotation;
        public bool HorizontalFlip;
        public bool VerticalFlip;
    }

    public sealed class FakeLine
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;
        public double Rotation;
        public bool HorizontalFlip;
        public bool VerticalFlip;
    }

    public sealed class FakeTextFrame
    {
        public readonly FakeTextRange TextRange = new FakeTextRange();
    }

    public sealed class FakeTextRange
    {
        public string Text;
    }

    public sealed class FakeTags
    {
        private readonly List<string> _names = new List<string>();
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int Count { get { return _names.Count; } }

        public string this[string name]
        {
            get
            {
                string value;
                return _values.TryGetValue(name, out value) ? value : null;
            }
            set
            {
                if (!_values.ContainsKey(name)) _names.Add(name);
                _values[name] = value;
            }
        }

        public string Name(int index) { return _names[index - 1]; }
        public string Value(int index) { return _values[_names[index - 1]]; }
    }
}
