# v2.5 动作素材记录

使用内置 ImageGen 工具。参考现有 qa/actions.png 的银狼形象，生成 2×2 行走／坐姿图集；未改动旧动作图集。

项目素材：assets/default/motion-v2.5.png。它是绿色底的原始生成图；角色包中的 greenScreen 设置让 WPF 在载入时处理透明背景。没有覆盖旧素材。

最终编辑提示词：

Edit this exact 2x2 sprite atlas. Replace ALL checkerboard background with a completely uniform pure chroma green #00FF00 background, including all gaps around hair, limbs and clothing. No checkerboard, no shadows, no gradient. Keep all character pixels/style/cell placement unchanged except TOP RIGHT walking pose must have the OTHER leg leading versus top left, to make two visibly distinct alternating walking steps; do not duplicate the top-left pose. Bottom row seated characters unchanged. Equal 2x2 cells, 1024x1536. Production chroma-key game sprite sheet.

检验产物：dist/SilverWolfPet-v2.5/qa/motion.png，展示实际透明处理后的两帧行走与两帧坐姿以及接触线。原始生成图的腿部姿态变化幅度较小，行走配合程序中的横移与轻微上下起伏播放。
