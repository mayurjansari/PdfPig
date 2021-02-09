﻿namespace UglyToad.PdfPig.Writer
{
    using Content;
    using Core;
    using Fonts;
    using Graphics.Colors;
    using Graphics.Operations;
    using Graphics.Operations.General;
    using Graphics.Operations.PathConstruction;
    using Graphics.Operations.SpecialGraphicsState;
    using Graphics.Operations.TextObjects;
    using Graphics.Operations.TextPositioning;
    using Graphics.Operations.TextShowing;
    using Graphics.Operations.TextState;
    using Images;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using PdfFonts;
    using Tokens;
    using Graphics.Operations.PathPainting;
    using Images.Png;

    /// <summary>
    /// A builder used to add construct a page in a PDF document.
    /// </summary>
    public class PdfPageBuilder
    {
        private readonly PdfDocumentBuilder documentBuilder;
        private readonly List<ContentStream> contentStreams;
        private readonly Dictionary<NameToken, IToken> resourcesDictionary = new Dictionary<NameToken, IToken>();

        //a sequence number of ShowText operation to determine whether letters belong to same operation or not (letters that belong to different operations have less changes to belong to same word)
        private int textSequence;

        private int imageKey = 1;

        internal IReadOnlyDictionary<NameToken, IToken> Resources => resourcesDictionary;

        /// <summary>
        /// The number of this page, 1-indexed.
        /// </summary>
        public int PageNumber { get; }

        /// <summary>
        /// The current size of the page.
        /// </summary>
        public PdfRectangle PageSize { get; set; }

        /// <summary>
        /// Access to the underlying data structures for advanced use cases.
        /// </summary>
        public ContentStream CurrentStream { get; private set; }

        /// <summary>
        /// Access to
        /// </summary>
        public IReadOnlyList<ContentStream> ContentStreams { get; }

        internal PdfPageBuilder(int number, PdfDocumentBuilder documentBuilder)
        {
            this.documentBuilder = documentBuilder ?? throw new ArgumentNullException(nameof(documentBuilder));
            PageNumber = number;

            CurrentStream = new ContentStream();
            ContentStreams = contentStreams = new List<ContentStream>()
            {
                CurrentStream
            };
        }

        /// <summary>
        /// Allow to append a new content stream before the current one and select it
        /// </summary>
        public void NewContentStreamBefore()
        {
            var index = Math.Max(contentStreams.IndexOf(CurrentStream) - 1, 0);

            CurrentStream = new ContentStream();
            contentStreams.Insert(index, CurrentStream);
        }

        /// <summary>
        /// Allow to append a new content stream after the current one and select it
        /// </summary>
        public void NewContentStreamAfter()
        {
            var index = Math.Min(contentStreams.IndexOf(CurrentStream) + 1, contentStreams.Count);

            CurrentStream = new ContentStream();
            contentStreams.Insert(index, CurrentStream);
        }

        /// <summary>
        /// Select a content stream from the list, by his index
        /// </summary>
        /// <param name="index">index of the content stream to be selected</param>
        public void SelectContentStream(int index)
        {
            if (index < 0 || index >= ContentStreams.Count)
            {
                throw new IndexOutOfRangeException(nameof(index));
            }

            CurrentStream = ContentStreams[index];
        }

        /// <summary>
        /// Draws a line on the current page between two points with the specified line width.
        /// </summary>
        /// <param name="from">The first point on the line.</param>
        /// <param name="to">The last point on the line.</param>
        /// <param name="lineWidth">The width of the line in user space units.</param>
        public void DrawLine(PdfPoint from, PdfPoint to, decimal lineWidth = 1)
        {
            if (lineWidth != 1)
            {
                CurrentStream.Add(new SetLineWidth(lineWidth));
            }

            CurrentStream.Add(new BeginNewSubpath((decimal)from.X, (decimal)from.Y));
            CurrentStream.Add(new AppendStraightLineSegment((decimal)to.X, (decimal)to.Y));
            CurrentStream.Add(StrokePath.Value);

            if (lineWidth != 1)
            {
                CurrentStream.Add(new SetLineWidth(1));
            }
        }

        /// <summary>
        /// Draws a rectangle on the current page starting at the specified point with the given width, height and line width.
        /// </summary>
        /// <param name="position">The position of the rectangle, for positive width and height this is the bottom-left corner.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="lineWidth">The width of the line border of the rectangle.</param>
        /// <param name="fill">Whether to fill with the color set by <see cref="SetTextAndFillColor"/>.</param>
        public void DrawRectangle(PdfPoint position, decimal width, decimal height, decimal lineWidth = 1, bool fill = false)
        {
            if (lineWidth != 1)
            {
                CurrentStream.Add(new SetLineWidth(lineWidth));
            }

            CurrentStream.Add(new AppendRectangle((decimal)position.X, (decimal)position.Y, width, height));

            if (fill)
            {
                CurrentStream.Add(FillPathEvenOddRuleAndStroke.Value);
            }
            else
            {
                CurrentStream.Add(StrokePath.Value);
            }

            if (lineWidth != 1)
            {
                CurrentStream.Add(new SetLineWidth(lineWidth));
            }
        }

        /// <summary>
        /// Sets the stroke color for any following operations to the RGB value. Use <see cref="ResetColor"/> to reset.
        /// </summary>
        /// <param name="r">Red - 0 to 255</param>
        /// <param name="g">Green - 0 to 255</param>
        /// <param name="b">Blue - 0 to 255</param>
        public void SetStrokeColor(byte r, byte g, byte b)
        {
            CurrentStream.Add(Push.Value);
            CurrentStream.Add(new SetStrokeColorDeviceRgb(RgbToDecimal(r), RgbToDecimal(g), RgbToDecimal(b)));
        }

        /// <summary>
        /// Sets the stroke color with the exact decimal value between 0 and 1 for any following operations to the RGB value. Use <see cref="ResetColor"/> to reset.
        /// </summary>
        /// <param name="r">Red - 0 to 1</param>
        /// <param name="g">Green - 0 to 1</param>
        /// <param name="b">Blue - 0 to 1</param>
        internal void SetStrokeColorExact(decimal r, decimal g, decimal b)
        {
            CurrentStream.Add(Push.Value);
            CurrentStream.Add(new SetStrokeColorDeviceRgb(CheckRgbDecimal(r, nameof(r)),
                CheckRgbDecimal(g, nameof(g)), CheckRgbDecimal(b, nameof(b))));
        }

        /// <summary>
        /// Sets the fill and text color for any following operations to the RGB value. Use <see cref="ResetColor"/> to reset.
        /// </summary>
        /// <param name="r">Red - 0 to 255</param>
        /// <param name="g">Green - 0 to 255</param>
        /// <param name="b">Blue - 0 to 255</param>
        public void SetTextAndFillColor(byte r, byte g, byte b)
        {
            CurrentStream.Add(Push.Value);
            CurrentStream.Add(new SetNonStrokeColorDeviceRgb(RgbToDecimal(r), RgbToDecimal(g), RgbToDecimal(b)));
        }

        /// <summary>
        /// Restores the stroke, text and fill color to default (black).
        /// </summary>
        public void ResetColor()
        {
            CurrentStream.Add(Pop.Value);
        }

        /// <summary>
        /// Calculates the size and position of each letter in a given string in the provided font without changing the state of the page. 
        /// </summary>
        /// <param name="text">The text to measure each letter of.</param>
        /// <param name="fontSize">The size of the font in user space units.</param>
        /// <param name="position">The position of the baseline (lower-left corner) to start drawing the text from.</param>
        /// <param name="font">
        /// A font added to the document using <see cref="PdfDocumentBuilder.AddTrueTypeFont"/>
        /// or <see cref="PdfDocumentBuilder.AddStandard14Font"/> methods.
        /// </param> 
        /// <returns>The letters from the input text with their corresponding size and position.</returns>
        public IReadOnlyList<Letter> MeasureText(string text, decimal fontSize, PdfPoint position, PdfDocumentBuilder.AddedFont font)
        {
            if (font == null)
            {
                throw new ArgumentNullException(nameof(font));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (!documentBuilder.Fonts.TryGetValue(font.Id, out var fontStore))
            {
                throw new ArgumentException($"No font has been added to the PdfDocumentBuilder with Id: {font.Id}. " +
                                            $"Use {nameof(documentBuilder.AddTrueTypeFont)} to register a font.", nameof(font));
            }

            if (fontSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be greater than 0");
            }

            var fontProgram = fontStore.FontProgram;

            var fm = fontProgram.GetFontMatrix();

            var textMatrix = TransformationMatrix.FromValues(1, 0, 0, 1, position.X, position.Y);

            var letters = DrawLetters(text, fontProgram, fm, fontSize, textMatrix);

            return letters;
        }

        /// <summary>
        /// Draws the text in the provided font at the specified position and returns the letters which will be drawn. 
        /// </summary>
        /// <param name="text">The text to draw to the page.</param>
        /// <param name="fontSize">The size of the font in user space units.</param>
        /// <param name="position">The position of the baseline (lower-left corner) to start drawing the text from.</param>
        /// <param name="font">
        /// A font added to the document using <see cref="PdfDocumentBuilder.AddTrueTypeFont"/>
        /// or <see cref="PdfDocumentBuilder.AddStandard14Font"/> methods.
        /// </param> 
        /// <returns>The letters from the input text with their corresponding size and position.</returns>
        public IReadOnlyList<Letter> AddText(string text, decimal fontSize, PdfPoint position, PdfDocumentBuilder.AddedFont font)
        {
            if (font == null)
            {
                throw new ArgumentNullException(nameof(font));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (!documentBuilder.Fonts.TryGetValue(font.Id, out var fontStore))
            {
                throw new ArgumentException($"No font has been added to the PdfDocumentBuilder with Id: {font.Id}. " +
                                            $"Use {nameof(documentBuilder.AddTrueTypeFont)} to register a font.", nameof(font));
            }

            if (fontSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be greater than 0");
            }

            var fontProgram = fontStore.FontProgram;

            var fm = fontProgram.GetFontMatrix();

            var textMatrix = TransformationMatrix.FromValues(1, 0, 0, 1, position.X, position.Y);

            var letters = DrawLetters(text, fontProgram, fm, fontSize, textMatrix);

            CurrentStream.Add(BeginText.Value);
            CurrentStream.Add(new SetFontAndSize(font.Name, fontSize));
            CurrentStream.Add(new MoveToNextLineWithOffset((decimal)position.X, (decimal)position.Y));
            var bytesPerShow = new List<byte>();
            foreach (var letter in text)
            {
                if (char.IsWhiteSpace(letter))
                {
                    CurrentStream.Add(new ShowText(bytesPerShow.ToArray()));
                    bytesPerShow.Clear();
                }

                var b = fontProgram.GetValueForCharacter(letter);
                bytesPerShow.Add(b);
            }

            if (bytesPerShow.Count > 0)
            {
                CurrentStream.Add(new ShowText(bytesPerShow.ToArray()));
            }

            CurrentStream.Add(EndText.Value);

            return letters;
        }

        /// <summary>
        /// Adds the JPEG image represented by the input bytes at the specified location.
        /// </summary>
        public AddedImage AddJpeg(byte[] fileBytes, PdfRectangle placementRectangle)
        {
            using (var stream = new MemoryStream(fileBytes))
            {
                return AddJpeg(stream, placementRectangle);
            }
        }
        
        /// <summary>
        /// Adds the JPEG image represented by the input stream at the specified location.
        /// </summary>
        public AddedImage AddJpeg(Stream fileStream, PdfRectangle placementRectangle)
        {
            var startFrom = fileStream.Position;
            var info = JpegHandler.GetInformation(fileStream);

            byte[] data;
            using (var memory = new MemoryStream())
            {
                fileStream.Seek(startFrom, SeekOrigin.Begin);
                fileStream.CopyTo(memory);
                data = memory.ToArray();
            }

            var imgDictionary = new Dictionary<NameToken, IToken>
            {
                {NameToken.Type, NameToken.Xobject },
                {NameToken.Subtype, NameToken.Image },
                {NameToken.Width, new NumericToken(info.Width) },
                {NameToken.Height, new NumericToken(info.Height) },
                {NameToken.BitsPerComponent, new NumericToken(info.BitsPerComponent)},
                {NameToken.ColorSpace, NameToken.Devicergb},
                {NameToken.Filter, NameToken.DctDecode},
                {NameToken.Length, new NumericToken(data.Length)}
            };

            var reference = documentBuilder.AddImage(new DictionaryToken(imgDictionary), data);

            if (!resourcesDictionary.TryGetValue(NameToken.Xobject, out var xobjectsDict) 
                || !(xobjectsDict is DictionaryToken xobjects))
            {
                xobjects = new DictionaryToken(new Dictionary<NameToken, IToken>());
                resourcesDictionary[NameToken.Xobject] = xobjects;
            }

            var key = NameToken.Create($"I{imageKey++}");

            resourcesDictionary[NameToken.Xobject] = xobjects.With(key, new IndirectReferenceToken(reference));

            CurrentStream.Add(Push.Value);
            // This needs to be the placement rectangle.
            CurrentStream.Add(new ModifyCurrentTransformationMatrix(new []
            {
                (decimal)placementRectangle.Width, 0,
                0, (decimal)placementRectangle.Height,
                (decimal)placementRectangle.BottomLeft.X, (decimal)placementRectangle.BottomLeft.Y
            }));
            CurrentStream.Add(new InvokeNamedXObject(key));
            CurrentStream.Add(Pop.Value);

            return new AddedImage(reference, info.Width, info.Height);
        }

        /// <summary>
        /// Adds the JPEG image previously added using <see cref="AddJpeg(byte[],PdfRectangle)"/>,
        /// this will share the same image data to prevent duplication.
        /// </summary>
        /// <param name="image">An image previously added to this page or another page.</param>
        /// <param name="placementRectangle">The size and location to draw the image on this page.</param>
        public void AddJpeg(AddedImage image, PdfRectangle placementRectangle) => AddImage(image, placementRectangle);

        /// <summary>
        /// Adds the image previously added using <see cref="AddJpeg(byte[], PdfRectangle)"/>
        /// or <see cref="AddPng(byte[], PdfRectangle)"/> sharing the same image to prevent duplication.
        /// </summary>
        public void AddImage(AddedImage image, PdfRectangle placementRectangle)
        {
            if (!resourcesDictionary.TryGetValue(NameToken.Xobject, out var xobjectsDict) 
                || !(xobjectsDict is DictionaryToken xobjects))
            {
                xobjects = new DictionaryToken(new Dictionary<NameToken, IToken>());
                resourcesDictionary[NameToken.Xobject] = xobjects;
            }

            var key = NameToken.Create($"I{imageKey++}");

            resourcesDictionary[NameToken.Xobject] = xobjects.With(key, new IndirectReferenceToken(image.Reference));

            CurrentStream.Add(Push.Value);
            // This needs to be the placement rectangle.
            CurrentStream.Add(new ModifyCurrentTransformationMatrix(new[]
            {
                (decimal)placementRectangle.Width, 0,
                0, (decimal)placementRectangle.Height,
                (decimal)placementRectangle.BottomLeft.X, (decimal)placementRectangle.BottomLeft.Y
            }));
            CurrentStream.Add(new InvokeNamedXObject(key));
            CurrentStream.Add(Pop.Value);
        }

        /// <summary>
        /// Adds the PNG image represented by the input bytes at the specified location.
        /// </summary>
        public AddedImage AddPng(byte[] pngBytes, PdfRectangle placementRectangle)
        {
            using (var memoryStream = new MemoryStream(pngBytes))
            {
                return AddPng(memoryStream, placementRectangle);
            }
        }

        /// <summary>
        /// Adds the PNG image represented by the input stream at the specified location.
        /// </summary>
        public AddedImage AddPng(Stream pngStream, PdfRectangle placementRectangle)
        {
            var png = Png.Open(pngStream);

            byte[] data;
            var pixelBuffer = new byte[3];
            using (var memoryStream = new MemoryStream())
            {
                for (var rowIndex = 0; rowIndex < png.Height; rowIndex++)
                {
                    for (var colIndex = 0; colIndex < png.Width; colIndex++)
                    {
                        var pixel = png.GetPixel(colIndex, rowIndex);

                        pixelBuffer[0] = pixel.R;
                        pixelBuffer[1] = pixel.G;
                        pixelBuffer[2] = pixel.B;

                        memoryStream.Write(pixelBuffer, 0, pixelBuffer.Length);
                    }
                }

                data = memoryStream.ToArray();
            }

            var compressed = DataCompresser.CompressBytes(data);

            var imgDictionary = new Dictionary<NameToken, IToken>
            {
                {NameToken.Type, NameToken.Xobject },
                {NameToken.Subtype, NameToken.Image },
                {NameToken.Width, new NumericToken(png.Width) },
                {NameToken.Height, new NumericToken(png.Height) },
                {NameToken.BitsPerComponent, new NumericToken(png.Header.BitDepth)},
                {NameToken.ColorSpace, NameToken.Devicergb},
                {NameToken.Filter, NameToken.FlateDecode},
                {NameToken.Length, new NumericToken(compressed.Length)}
            };
            
            var reference = documentBuilder.AddImage(new DictionaryToken(imgDictionary), compressed);

            if (!resourcesDictionary.TryGetValue(NameToken.Xobject, out var xobjectsDict)
                || !(xobjectsDict is DictionaryToken xobjects))
            {
                xobjects = new DictionaryToken(new Dictionary<NameToken, IToken>());
                resourcesDictionary[NameToken.Xobject] = xobjects;
            }

            var key = NameToken.Create($"I{imageKey++}");

            resourcesDictionary[NameToken.Xobject] = xobjects.With(key, new IndirectReferenceToken(reference));

            CurrentStream.Add(Push.Value);
            // This needs to be the placement rectangle.
            CurrentStream.Add(new ModifyCurrentTransformationMatrix(new[]
            {
                (decimal)placementRectangle.Width, 0,
                0, (decimal)placementRectangle.Height,
                (decimal)placementRectangle.BottomLeft.X, (decimal)placementRectangle.BottomLeft.Y
            }));
            CurrentStream.Add(new InvokeNamedXObject(key));
            CurrentStream.Add(Pop.Value);

            return new AddedImage(reference, png.Width, png.Height);
        }

        /// <summary>
        /// Copy a page from unknown source to this page
        /// </summary>
        /// <param name="srcPage">Page to be copied</param>
        public void CopyFrom(Page srcPage)
        {
            ContentStream destinationStream = null;
            if (CurrentStream.Operations.Count > 0)
            {
                NewContentStreamAfter();
            }

            destinationStream = CurrentStream;

            if (!srcPage.Dictionary.TryGet(NameToken.Resources, srcPage.pdfScanner, out DictionaryToken srcResourceDictionary))
            {
                // If the page doesn't have resources, then we copy the entire content stream, since not operation would collide 
                // with the ones already written
                destinationStream.Operations.AddRange(srcPage.Operations);
                return;
            }

            // TODO: How should we handle any other token in the page dictionary (Eg. LastModified, MediaBox, CropBox, BleedBox, TrimBox, ArtBox,
            //      BoxColorInfo, Rotate, Group, Thumb, B, Dur, Trans, Annots, AA, Metadata, PieceInfo, StructParents, ID, PZ, SeparationInfo, Tabs,
            //      TemplateInstantiated, PresSteps, UserUnit, VP)

            var operations = new List<IGraphicsStateOperation>(srcPage.Operations);

            // We need to relocate the resources, and we have to make sure that none of the resources collide with 
            // the already written operation's resources

            foreach (var set in srcResourceDictionary.Data)
            {
                var nameToken = NameToken.Create(set.Key);
                if (nameToken == NameToken.Font || nameToken == NameToken.Xobject)
                {
                    // We have to skip this two because we have a separate dictionary for them
                    continue;
                }

                if (!resourcesDictionary.TryGetValue(nameToken, out var currentToken))
                {
                    // It means that this type of resources doesn't currently exist in the page, so we can copy it
                    // with no problem
                    resourcesDictionary[nameToken] = documentBuilder.CopyToken(set.Value, srcPage.pdfScanner);
                    continue;
                }

                // TODO: I need to find a test case
                // It would have ExtendedGraphics or colorspaces, etc...
            }

            // Special cases
            // Since we don't directly add font's to the pages resources, we have to go look at the document's font
            if(srcResourceDictionary.TryGet(NameToken.Font, srcPage.pdfScanner, out DictionaryToken fontsDictionary))
            {
                Dictionary<NameToken, IToken> pageFontsDictionary = null;
                if (resourcesDictionary.TryGetValue(NameToken.Font, out var pageFontsToken))
                {
                    pageFontsDictionary = (pageFontsToken as DictionaryToken)?.Data.ToDictionary(k => NameToken.Create(k.Key), v => v.Value);
                    Debug.Assert(pageFontsDictionary != null);
                }
                else
                {
                    pageFontsDictionary = new Dictionary<NameToken, IToken>();
                }

                foreach (var fontSet in fontsDictionary.Data)
                {
                    var fontName = fontSet.Key;
                    var addedFont = documentBuilder.Fonts.Values.FirstOrDefault(f => f.FontKey.Name.Data == fontName);
                    if (addedFont != default)
                    {
                        // This would mean that the imported font collide with one of the added font. so we have to rename it

                        var newName = $"F{documentBuilder.fontId++}";

                        // Set all the pertinent SetFontAndSize operations with the new name
                        operations = operations.Select(op =>
                        {
                            if (!(op is SetFontAndSize fontAndSizeOperation))
                            {
                                return op;
                            }

                            if (fontAndSizeOperation.Font.Data == fontName)
                            {
                                return new SetFontAndSize(NameToken.Create(newName), fontAndSizeOperation.Size);
                            }

                            return op;
                        }).ToList();

                        fontName = newName;
                    }

                    if (!(fontSet.Value is IndirectReferenceToken fontReferenceToken))
                    {
                        throw new PdfDocumentFormatException($"Expected a IndirectReferenceToken for the font, got a {fontSet.Value.GetType().Name}");
                    }

                    pageFontsDictionary.Add(NameToken.Create(fontName), documentBuilder.CopyToken(fontReferenceToken, srcPage.pdfScanner));
                }

                resourcesDictionary[NameToken.Font] = new DictionaryToken(pageFontsDictionary);
            }

            // Since we don't directly add xobjects's to the pages resources, we have to go look at the document's xobjects
            if (srcResourceDictionary.TryGet(NameToken.Xobject, srcPage.pdfScanner, out DictionaryToken xobjectsDictionary))
            {
                Dictionary<NameToken, IToken> pageXobjectsDictionary = null;
                if (resourcesDictionary.TryGetValue(NameToken.Xobject, out var pageXobjectToken))
                {
                    pageXobjectsDictionary = (pageXobjectToken as DictionaryToken)?.Data.ToDictionary(k => NameToken.Create(k.Key), v => v.Value);
                    Debug.Assert(pageXobjectsDictionary != null);
                }
                else
                {
                    pageXobjectsDictionary = new Dictionary<NameToken, IToken>();
                }

                var xobjectNamesUsed = Enumerable.Range(0, imageKey).Select(i => $"I{i}");
                foreach (var xobjectSet in xobjectsDictionary.Data)
                {
                    var xobjectName = xobjectSet.Key;
                    if (xobjectName[0] == 'I' && xobjectNamesUsed.Any(s => s == xobjectName))
                    {
                        // This would mean that the imported xobject collide with one of the added image. so we have to rename it
                        var newName = $"I{imageKey++}";

                        // Set all the pertinent SetFontAndSize operations with the new name
                        operations = operations.Select(op =>
                        {
                            if (!(op is InvokeNamedXObject invokeNamedOperation))
                            {
                                return op;
                            }

                            if (invokeNamedOperation.Name.Data == xobjectName)
                            {
                                return new InvokeNamedXObject(NameToken.Create(newName));
                            }

                            return op;
                        }).ToList();

                        xobjectName = newName;
                    }

                    if (!(xobjectSet.Value is IndirectReferenceToken fontReferenceToken))
                    {
                        throw new PdfDocumentFormatException($"Expected a IndirectReferenceToken for the XObject, got a {xobjectSet.Value.GetType().Name}");
                    }

                    pageXobjectsDictionary.Add(NameToken.Create(xobjectName), documentBuilder.CopyToken(fontReferenceToken, srcPage.pdfScanner));
                }

                resourcesDictionary[NameToken.Xobject] = new DictionaryToken(pageXobjectsDictionary);
            }

            destinationStream.Operations.AddRange(operations);
        }

        private List<Letter> DrawLetters(string text, IWritingFont font, TransformationMatrix fontMatrix, decimal fontSize, TransformationMatrix textMatrix)
        {
            var horizontalScaling = 1;
            var rise = 0;
            var letters = new List<Letter>();

            var renderingMatrix =
                TransformationMatrix.FromValues((double)fontSize * horizontalScaling, 0, 0, (double)fontSize, 0, rise);

            var width = 0.0;

            textSequence++;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (!font.TryGetBoundingBox(c, out var rect))
                {
                    throw new InvalidOperationException($"The font does not contain a character: {c}.");
                }

                if (!font.TryGetAdvanceWidth(c, out var charWidth))
                {
                    throw new InvalidOperationException($"The font does not contain a character: {c}.");
                }

                var advanceRect = new PdfRectangle(0, 0, charWidth, 0);
                advanceRect = textMatrix.Transform(renderingMatrix.Transform(fontMatrix.Transform(advanceRect)));

                var documentSpace = textMatrix.Transform(renderingMatrix.Transform(fontMatrix.Transform(rect)));

                var letter = new Letter(c.ToString(), documentSpace, advanceRect.BottomLeft, advanceRect.BottomRight, width, (double)fontSize, FontDetails.GetDefault(font.Name),
                    GrayColor.Black,
                    (double)fontSize,
                    textSequence);

                letters.Add(letter);

                var tx = advanceRect.Width * horizontalScaling;
                var ty = 0;

                var translate = TransformationMatrix.GetTranslationMatrix(tx, ty);

                width += tx;

                textMatrix = translate.Multiply(textMatrix);
            }

            return letters;
        }

        private static decimal RgbToDecimal(byte value)
        {
            var res = Math.Max(0, value / (decimal)byte.MaxValue);
            res = Math.Round(Math.Min(1, res), 4);

            return res;
        }

        private static decimal CheckRgbDecimal(decimal value, string argument)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(argument, $"Provided decimal for RGB color was less than zero: {value}.");
            }

            if (value > 1)
            {
                throw new ArgumentOutOfRangeException(argument, $"Provided decimal for RGB color was greater than one: {value}.");
            }

            return value;
        }

        /// <summary>
        /// Provides access to the raw page data structures for advanced editing use cases.
        /// </summary>
        public class ContentStream
        {
            /// <summary>
            /// The operations making up the page content stream.
            /// </summary>
            public List<IGraphicsStateOperation> Operations { get; }

            /// <summary>
            /// Create a new <see cref="ContentStream"/>.
            /// </summary>
            internal ContentStream()
            {
                Operations = new List<IGraphicsStateOperation>();
            }

            internal void Add(IGraphicsStateOperation newOperation)
            {
                Operations.Add(newOperation);
            }
        }

        /// <summary>
        /// A key representing an image available to use for the current document builder.
        /// Create it by adding an image to a page using <see cref="AddJpeg(byte[],PdfRectangle)"/>.
        /// </summary>
        public class AddedImage
        {
            /// <summary>
            /// The Id uniquely identifying this image on the builder.
            /// </summary>
            internal Guid Id { get; }

            /// <summary>
            /// The reference to the stored image XObject.
            /// </summary>
            internal IndirectReference Reference { get; }

            /// <summary>
            /// The width of the raw image in pixels.
            /// </summary>
            public int Width { get; }

            /// <summary>
            /// The height of the raw image in pixels.
            /// </summary>
            public int Height { get; }

            /// <summary>
            /// Create a new <see cref="AddedImage"/>.
            /// </summary>
            internal AddedImage(IndirectReference reference, int width, int height)
            {
                Id = Guid.NewGuid();
                Reference = reference;
                Width = width;
                Height = height;
            }
        }
    }
}