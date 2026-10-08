# 贴边动态素材库

## 2.6.30 新增随机表情

新增 `observe.png`（暗中观察）、`smug.png`（得意）、`puzzled.png`（疑惑）、`snicker.png`（偷笑）。与开心表情一起约每 25–50 秒随机出现，避免连续重复。无操作超过 90 秒后有一半概率选择瞌睡（也不连续重复），其余时间使用随机表情。眨眼不会推迟随机动作。每组约 2 秒，表情帧与原待机、闭眼帧组合播放，回到原图。

采用内置 imagegen 以 `../peek-right.png` 为唯一编辑目标，以下为完整提示词公共部分和各图追加部分：

Edit this EXACT existing transparent chibi Silver Wolf screen-edge sprite for animation. Preserve pixel composition, canvas size, character position/scale, outer silhouette, hair, angled forehead goggles, hands, clothing, head tilt, every outline and all transparent margins. Change ONLY facial eyes, eyebrows and mouth as described. Same illustration, not a redraw or new interpretation. No head movement, no new decoration, no bean, no badge, no UI, no text. True alpha transparency. Single image not sprite sheet.

observe: Expression: secretly observing, narrowed half-lidded eyes looking toward viewer left, slightly suspicious eyebrows, tiny pursed mouth. Cute mischievous meme.

smug: Expression: smug proud little gamer. One eye winks closed, other confident half-lidded eye, one eyebrow raised and small asymmetric self-satisfied smile. Cute meme.

puzzled: Expression: adorably confused. One eyebrow raised and other lowered, wide eyes looking slightly upward, tiny round questioning mouth. No question mark or extra shapes.

snicker: Expression: trying not to laugh. Both eyes tightly curved into playful upward crescents, tiny toothy mischievous grin, slightly rosy cheeks. Keep both hands exactly in place.

运行时预览 GIF 位于 `../previews/`，由应用自测按相同素材和时长绘制。

基准为上一版 `../peek-right.png`，保留原护目镜、扒边姿势和画布。图像由内置 imagegen 编辑，真透明背景。图片不含额度文字，程序继续独立绘制骇客豆与额度入口。

- `blink.png`：自然合眼，嘴型保持。用于短眨眼。
- `happy.png`：月牙笑眼和开心张嘴，用于点击回应和右键预览。
- `sleepy.png`：安静闭眼、小圆嘴，用于打瞌睡。

`../library.json` 定义帧文件、播放顺序及毫秒时长；原图作为睁眼待机／过渡帧重复使用。角色自动动作使用已有计时器，动画中 50ms 更新，静止时 200ms 检查；所有图片解码宽度限制为 256，缓存复用。角色占用仍为 84 × 144 逻辑像素。

生成提示词公共部分：

Edit this EXACT existing transparent chibi Silver Wolf screen-edge sprite for animation. Preserve pixel composition, canvas size, character position/scale, outer silhouette, hair, angled forehead goggles, hands, clothing, head tilt, every outline and all transparent margins. Change ONLY eyelids and mouth as described. Same illustration, not a redraw or new interpretation. No head movement, no new decoration, no bean, no badge, no UI, no text. True alpha transparency. Single image not sprite sheet.

Blink: Both eyes gently fully closed for one natural blink, relaxed shallow curved downward lash lines following current tilted face. Mouth stays original small cat smile. No happy eyebrow change.

Happy: Both eyes become joyful upward crescent smiles, cheeks slightly pinker, small open happy smiling mouth with dark pink inside, subtle playful laugh. Keep all face geometry fixed.

Sleepy: Both eyes relaxed fully closed, brows relaxed, tiny small rounded sleepy mouth as if softly exhaling, peaceful dozing face. No snore text, no bubbles, no droplets.
