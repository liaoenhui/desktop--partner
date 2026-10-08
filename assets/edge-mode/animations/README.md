# 贴边动态素材库

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
