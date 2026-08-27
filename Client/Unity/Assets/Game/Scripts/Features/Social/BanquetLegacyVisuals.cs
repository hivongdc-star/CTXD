using System.Collections.Generic;
using CTXD.Client.Features.FirstPlayable;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Social
{
    internal static class BanquetLegacyVisuals
    {
        const string Root = "LegacyVisual/Banquet/";

        readonly struct Region
        {
            public readonly string texture;
            public readonly int x;
            public readonly int y;
            public readonly int width;
            public readonly int height;

            public Region(string texture, int x, int y, int width, int height)
            {
                this.texture = texture;
                this.x = x;
                this.y = y;
                this.width = width;
                this.height = height;
            }
        }

        // Coordinates are top-left based and come from the lossless packing step only; source pixels are unchanged.
        static readonly Dictionary<string, Region> Regions = new()
        {
            ["rules_bg"] = new Region("ui_atlas", 0, 0, 685, 352),
            ["dialog_bg"] = new Region("ui_atlas", 685, 0, 635, 351),
            ["enter_bg"] = new Region("ui_atlas", 1320, 0, 596, 263),
            ["organizer_lobby"] = new Region("ui_atlas", 0, 352, 1024, 80),
            ["organizer_room"] = new Region("ui_atlas", 0, 432, 1600, 112),
            ["participant_a"] = new Region("ui_atlas", 0, 544, 1600, 96),
            ["participant_b"] = new Region("ui_atlas", 0, 640, 1600, 104),
            ["rank_titles"] = new Region("ui_atlas", 0, 744, 500, 40),
            ["nation_icons"] = new Region("ui_atlas", 500, 744, 96, 32),
            ["item_icons"] = new Region("ui_atlas", 596, 744, 720, 80),
            ["enter_buttons"] = new Region("ui_atlas", 0, 824, 960, 24),
            ["floor_titles"] = new Region("ui_atlas", 0, 848, 1755, 196),
            ["result_base"] = new Region("ui_atlas", 0, 1044, 600, 180),
            ["drink_circle_parts"] = new Region("ui_atlas", 600, 1044, 256, 128),
            ["result_titles"] = new Region("effects_atlas", 0, 0, 1800, 540),
            ["result_fx"] = new Region("effects_atlas", 0, 540, 1800, 540),
            ["countdown_frames"] = new Region("effects_atlas", 0, 1080, 1800, 640)
        };

        static readonly Dictionary<string, Texture2D> Textures = new();
        static readonly Dictionary<string, Sprite> Sprites = new();

        static Texture2D Texture(string name)
        {
            if (Textures.TryGetValue(name, out var cached) && cached != null) return cached;
            var texture = Resources.Load<Texture2D>(Root + name);
            Textures[name] = texture;
            return texture;
        }

        static bool Resolve(string name, out Texture2D texture, out Region region)
        {
            if (Regions.TryGetValue(name, out region))
            {
                texture = Texture(region.texture);
                return texture != null;
            }
            texture = Texture(name);
            if (texture == null) return false;
            region = new Region(name, 0, 0, texture.width, texture.height);
            return true;
        }

        public static Sprite FullSprite(string name)
        {
            var key = name + ":full";
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!Resolve(name, out var texture, out var region)) return null;
            var y = texture.height - region.y - region.height;
            var sprite = Sprite.Create(texture, new Rect(region.x, y, region.width, region.height), new Vector2(.5f, .5f), 100f);
            sprite.name = "Banquet_" + name;
            Sprites[key] = sprite;
            return sprite;
        }

        public static Sprite CellSprite(string name, int index, int cellWidth, int cellHeight)
        {
            var key = name + ":cell:" + index + ":" + cellWidth + "x" + cellHeight;
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!Resolve(name, out var texture, out var region)) return null;
            var columns = Mathf.Max(1, region.width / cellWidth);
            var column = index % columns;
            var row = index / columns;
            var x = region.x + column * cellWidth;
            var y = texture.height - region.y - ((row + 1) * cellHeight);
            var sprite = Sprite.Create(texture, new Rect(x, y, cellWidth, cellHeight), new Vector2(.5f, .5f), 100f);
            sprite.name = "Banquet_" + name + "_" + index;
            Sprites[key] = sprite;
            return sprite;
        }

        public static Sprite TopLeftCellSprite(string name, int index, int cellWidth, int cellHeight, int contentWidth, int contentHeight)
        {
            var key = name + ":crop:" + index + ":" + contentWidth + "x" + contentHeight;
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!Resolve(name, out var texture, out var region)) return null;
            var columns = Mathf.Max(1, region.width / cellWidth);
            var column = index % columns;
            var row = index / columns;
            var x = region.x + column * cellWidth;
            var y = texture.height - region.y - (row * cellHeight) - contentHeight;
            var sprite = Sprite.Create(texture, new Rect(x, y, contentWidth, contentHeight), new Vector2(.5f, .5f), 100f);
            sprite.name = "Banquet_" + name + "_" + index;
            Sprites[key] = sprite;
            return sprite;
        }

        public static Image FullImage(Transform parent, string name, float x, float y, float width, float height) =>
            Image(parent, FullSprite(name), x, y, width, height);

        public static Image CellImage(Transform parent, string name, int index, int cellWidth, int cellHeight, float x, float y, float width, float height) =>
            Image(parent, CellSprite(name, index, cellWidth, cellHeight), x, y, width, height);

        public static Image TopLeftCellImage(Transform parent, string name, int index, int cellWidth, int cellHeight, int contentWidth, int contentHeight, float x, float y) =>
            Image(parent, TopLeftCellSprite(name, index, cellWidth, cellHeight, contentWidth, contentHeight), x, y, contentWidth, contentHeight);

        static Image Image(Transform parent, Sprite sprite, float x, float y, float width, float height)
        {
            var root = LegacyUiFactory.PixelPanel(parent, "BanquetLegacyVisual", x, y, width, height, Color.clear);
            var image = root.GetComponent<Image>();
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.color = sprite == null ? Color.clear : Color.white;
            image.raycastTarget = false;
            return image;
        }

        public static void SetCell(Image image, string name, int index, int cellWidth, int cellHeight)
        {
            if (image != null) image.sprite = CellSprite(name, index, cellWidth, cellHeight);
        }
    }
}
