from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "codex-mode"
FRAMES = SOURCE / "frames"
PREVIEWS = SOURCE / "previews"


def cells(path, columns, rows):
    image = Image.open(path).convert("RGBA")
    result = []
    for row in range(rows):
        for column in range(columns):
            left = round(column * image.width / columns)
            right = round((column + 1) * image.width / columns)
            top = round(row * image.height / rows)
            bottom = round((row + 1) * image.height / rows)
            result.append(image.crop((left, top, right, bottom)))
    return result


def normalize(images):
    boxes = [image.getchannel("A").getbbox() for image in images]
    if any(box is None for box in boxes):
        raise ValueError("A generated Codex-mode cell is empty")
    max_width = max(box[2] - box[0] for box in boxes)
    max_height = max(box[3] - box[1] for box in boxes)
    scale = min(480 / max_width, 470 / max_height)
    output = []
    for image, box in zip(images, boxes):
        crop = image.crop(box)
        resized = crop.resize(
            (round(crop.width * scale), round(crop.height * scale)),
            Image.Resampling.LANCZOS,
        )
        canvas = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
        canvas.alpha_composite(resized, ((512 - resized.width) // 2, 500 - resized.height))
        output.append(canvas)
    return output


def atlas(images, columns, rows, path):
    canvas = Image.new("RGBA", (columns * 512, rows * 512), (0, 0, 0, 0))
    for index, image in enumerate(images):
        canvas.alpha_composite(image, ((index % columns) * 512, (index // columns) * 512))
    canvas.save(path)


def gif(name, frames, durations, loop=0):
    frames[0].save(
        PREVIEWS / name,
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        disposal=2,
        loop=loop,
        optimize=False,
    )


def main():
    FRAMES.mkdir(parents=True, exist_ok=True)
    PREVIEWS.mkdir(parents=True, exist_ok=True)
    master_names = [
        "daily", "transform_start", "transform_finish",
        "working_a", "working_b", "permission_master",
        "question_master", "failed_master", "success_master",
    ]
    loop_names = [
        "permission_a", "permission_b", "question_a", "question_b",
        "failed_a", "failed_b", "success_a", "success_b",
    ]
    all_images = cells(SOURCE / "codex-mode-master.png", 3, 3)
    all_images += cells(SOURCE / "codex-mode-loops.png", 4, 2)
    all_images = normalize(all_images)
    master = all_images[:9]
    loops = all_images[9:]
    named = dict(zip(master_names + loop_names, master + loops))
    for name, image in named.items():
        image.save(FRAMES / (name + ".png"))
    atlas(master, 3, 3, SOURCE / "codex-mode-states.png")
    atlas(loops, 4, 2, SOURCE / "codex-mode-motion.png")
    gif("transform.gif", [named[x] for x in ("daily", "transform_start", "transform_finish")], [260, 210, 720])
    gif("working.gif", [named[x] for x in ("working_a", "working_b")], [420, 420])
    gif("permission.gif", [named[x] for x in ("permission_a", "permission_b")], [680, 680])
    gif("question.gif", [named[x] for x in ("question_a", "question_b")], [620, 620])
    gif("failed.gif", [named[x] for x in ("failed_a", "failed_b")], [480, 480])
    gif("success.gif", [named[x] for x in ("success_a", "success_b")], [280, 520])
    gif("revert.gif", [named[x] for x in ("transform_finish", "transform_start", "daily")], [220, 220, 700], loop=1)
    print("Built", len(named), "Codex-mode frames in", SOURCE)


if __name__ == "__main__":
    main()
