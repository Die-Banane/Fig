using System;
using System.Collections.Generic;
using Fig.Core.Timeline;
using static Fig.Core.Media.CreativeFrameOps;

namespace Fig.Core.Media
{
    [Primitive("color-matrix", "Color matrix", Category = "Color", Description = "Brightness, contrast and saturation in one step.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("brightness", "Brightness", Default = 0, Min = -1, Max = 1)]
    [PrimitiveParam("contrast", "Contrast", Default = 0, Min = -1, Max = 1)]
    [PrimitiveParam("saturation", "Saturation", Default = 0, Min = -1, Max = 1)]
    internal sealed class ColorMatrixPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            double brightness = c.Number("brightness"), contrast = c.Number("contrast"), saturation = c.Number("saturation");
            if (brightness == 0 && contrast == 0 && saturation == 0) return src;
            double k = 1 + contrast, add = 128 * (1 - k) + brightness * 255, sat = 1 + saturation;
            var width = src.Width;
            var input = src.Pixels;
            return PrimitiveMath.Fill(src, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    byte b = input[i], g = input[i + 1], r = input[i + 2];
                    var luma = PrimitiveMath.Luma(b, g, r);
                    px[i] = Byte((luma + (b - luma) * sat) * k + add);
                    px[i + 1] = Byte((luma + (g - luma) * sat) * k + add);
                    px[i + 2] = Byte((luma + (r - luma) * sat) * k + add);
                    px[i + 3] = input[i + 3];
                }
            });
        }
    }

    [Primitive("levels", "Levels", Category = "Color", Description = "Set the black point, white point and midtones.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("black", "Black point", Default = 0, Min = 0, Max = 0.95)]
    [PrimitiveParam("white", "White point", Default = 1, Min = 0.05, Max = 1)]
    [PrimitiveParam("gamma", "Midtones", Default = 1, Min = 0.2, Max = 3)]
    internal sealed class LevelsPrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            double black = c.Number("black"), white = c.Number("white"), gamma = c.Number("gamma");
            if (white <= black + 0.01) white = black + 0.01;
            if (black == 0 && white == 1 && Math.Abs(gamma - 1) < 1e-6) return src;
            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                var v = Math.Clamp((i / 255.0 - black) / (white - black), 0, 1);
                lut[i] = Byte(Math.Pow(v, 1 / gamma) * 255);
            }
            var width = src.Width;
            var input = src.Pixels;
            return PrimitiveMath.Fill(src, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    px[i] = lut[input[i]]; px[i + 1] = lut[input[i + 1]]; px[i + 2] = lut[input[i + 2]]; px[i + 3] = input[i + 3];
                }
            });
        }
    }

    [Primitive("duotone", "Duotone", Category = "Color", Description = "Map shadows and highlights to two colors.")]
    [PrimitiveInput("image", "Image")]
    [PrimitiveOutput("image", "Image")]
    [PrimitiveParam("amount", "Blend", Default = 1, Min = 0, Max = 1)]
    [PrimitiveParam("shadows", "Shadows", Kind = ParamKind.Color, Default = 0xFF14284D)]
    [PrimitiveParam("highlights", "Highlights", Kind = ParamKind.Color, Default = 0xFFFFC857)]
    internal sealed class DuotonePrimitive : ImagePrimitive
    {
        protected override DecodedFrame Run(PrimitiveContext c)
        {
            var src = c.Input("image");
            var amount = c.Number("amount");
            if (amount <= 0) return src;
            uint low = c.Color("shadows"), high = c.Color("highlights");
            var width = src.Width;
            var input = src.Pixels;
            return PrimitiveMath.Fill(src, (px, start, _) =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = start + x * 4;
                    var luma = PrimitiveMath.Luma(input[i], input[i + 1], input[i + 2]) / 255;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        var target = ((low >> (ch * 8)) & 255) * (1 - luma) + ((high >> (ch * 8)) & 255) * luma;
                        px[i + ch] = Byte(input[i + ch] * (1 - amount) + target * amount);
                    }
                    px[i + 3] = input[i + 3];
                }
            });
        }
    }
}
