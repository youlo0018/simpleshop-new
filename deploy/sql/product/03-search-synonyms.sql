-- 商品搜索同义词。全局配置，保存后由 ProductService 重建索引并补写商品。
CREATE TABLE IF NOT EXISTS search_synonym (
    id          bigint       NOT NULL,
    terms       varchar(512) NOT NULL,
    sort_order  int          NOT NULL DEFAULT 0,
    rule_type   int          NOT NULL DEFAULT 1,
    scope_type  int          NOT NULL DEFAULT 1,
    scope_id    bigint       NOT NULL DEFAULT 0,
    priority    int          NOT NULL DEFAULT 100,
    status      int          NOT NULL DEFAULT 1,
    remark      varchar(256) NOT NULL DEFAULT '',
    created_at  timestamp    NOT NULL,
    updated_at  timestamp    NULL,
    is_deleted  boolean      NOT NULL DEFAULT false,
    deleted_at  timestamp    NULL,
    CONSTRAINT pk_search_synonym PRIMARY KEY (id)
);

ALTER TABLE search_synonym
    ADD COLUMN IF NOT EXISTS rule_type  int          NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS scope_type int          NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS scope_id   bigint       NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS priority   int          NOT NULL DEFAULT 100,
    ADD COLUMN IF NOT EXISTS status     int          NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS remark     varchar(256) NOT NULL DEFAULT '';

DROP INDEX IF EXISTS idx_search_synonym_sort;
DROP INDEX IF EXISTS uk_search_synonym_terms;

CREATE INDEX IF NOT EXISTS idx_search_synonym_scope
    ON search_synonym (scope_type, scope_id, status, priority, id)
    WHERE is_deleted = false;

CREATE UNIQUE INDEX IF NOT EXISTS uk_search_synonym_scoped_rule
    ON search_synonym (scope_type, scope_id, rule_type, terms)
    WHERE is_deleted = false;

-- 旧版内置组已并入完整默认词库；先删掉旧行，确保排序与当前词库一致。
BEGIN;

DELETE FROM search_synonym
WHERE is_deleted = false
  AND (terms, sort_order) IN (
      ('睫毛,眼睫毛', 10),
      ('睫毛膏,眼睫毛膏', 20),
      ('眼影,眼影盘', 30),
      ('香水,香氛', 40),
      ('口红,唇膏', 50),
      ('笔记本,笔记本电脑', 60)
  );

-- 默认词库参考 HIT 同义词词林、AitSimwords 等公开中文同义词资料后，
-- 按商品搜索场景人工筛选；只保留商品、类目和常见销售叫法。
WITH defaults(terms, sort_order) AS (
    VALUES
    ('美妆,彩妆,化妆品', 10),
    ('护肤,护肤品,护肤产品', 20),
    ('睫毛,眼睫毛', 30),
    ('睫毛膏,眼睫毛膏', 40),
    ('假睫毛,人造睫毛', 50),
    ('眼影,眼影盘', 60),
    ('口红,唇膏', 70),
    ('唇釉,液体唇膏', 80),
    ('润唇膏,护唇膏', 90),
    ('粉底,粉底液', 100),
    ('散粉,定妆粉', 110),
    ('遮瑕,遮瑕膏', 120),
    ('腮红,胭脂', 130),
    ('眼线,眼线笔', 140),
    ('眉笔,画眉笔', 150),
    ('卸妆水,卸妆液', 160),
    ('洗面奶,洁面乳,洁面', 170),
    ('爽肤水,化妆水', 180),
    ('精华,精华液', 190),
    ('面霜,护肤霜', 200),
    ('乳液,润肤乳', 210),
    ('眼霜,眼部霜', 220),
    ('面膜,面贴膜', 230),
    ('防晒,防晒霜,防晒乳', 240),
    ('香水,香氛', 250),
    ('女香,女士香水', 260),
    ('男香,男士香水', 270),
    ('指甲油,美甲油', 280),
    ('美妆蛋,化妆海绵', 290),
    ('化妆刷,彩妆刷', 300),
    ('洗发水,洗发露,洗发精,洗头膏', 310),
    ('护发素,润发乳', 320),
    ('发膜,护发膜', 330),
    ('沐浴露,沐浴乳', 340),
    ('身体乳,身体润肤乳', 350),
    ('护手霜,手霜', 360),
    ('剃须刀,刮胡刀', 370),
    ('吹风机,电吹风', 380),
    ('美容仪,美容器', 390),
    ('数码,数码产品', 400),
    ('手机,智能手机,移动电话', 410),
    ('电脑,计算机,个人电脑,微机', 420),
    ('笔记本,笔记本电脑,便携式电脑,手提电脑', 430),
    ('平板,平板电脑', 440),
    ('显示器,电脑显示器,显示屏', 450),
    ('耳机,耳筒', 460),
    ('蓝牙耳机,无线耳机', 470),
    ('音箱,扬声器,喇叭', 480),
    ('智能音箱,智能音响', 490),
    ('键盘,电脑键盘', 500),
    ('鼠标,鼠标器', 510),
    ('充电宝,移动电源', 520),
    ('充电器,充电头', 530),
    ('数据线,充电线', 540),
    ('手机壳,手机套,手机保护壳', 550),
    ('手机膜,屏幕保护膜', 560),
    ('U盘,优盘', 570),
    ('移动硬盘,便携硬盘', 580),
    ('摄像头,网络摄像头', 590),
    ('智能手表,智能腕表', 600),
    ('路由器,无线路由器', 610),
    ('家电,家用电器', 620),
    ('电视,电视机', 630),
    ('空调,空调器', 640),
    ('冰箱,电冰箱', 650),
    ('电饭煲,电饭锅', 660),
    ('扫地机器人,扫拖机器人', 670),
    ('电动牙刷,电牙刷', 680),
    ('电风扇,风扇', 690),
    ('加湿器,空气加湿器', 700),
    ('净水器,净水机', 710),
    ('热水器,电热水器', 720),
    ('空气净化器,空气净化机', 730),
    ('服装,衣服,衣物', 740),
    ('T恤,T-shirt,tee', 750),
    ('连衣裙,连身裙', 760),
    ('半身裙,裙子', 770),
    ('牛仔裤,jeans', 780),
    ('卫衣,连帽衫', 790),
    ('衬衫,衬衣', 800),
    ('羽绒服,羽绒衣', 810),
    ('外套,外衣', 820),
    ('针织衫,毛线衣', 830),
    ('运动鞋,跑鞋,球鞋,跑步鞋', 840),
    ('帆布鞋,布鞋', 850),
    ('皮鞋,革履', 860),
    ('拖鞋,凉拖', 870),
    ('高跟鞋,高跟鞋子', 880),
    ('袜子,袜', 890),
    ('帽子,帽', 900),
    ('围巾,围脖', 910),
    ('内衣,贴身衣物', 920),
    ('文胸,胸罩', 930),
    ('睡衣,寝衣', 940),
    ('泳衣,泳装', 950),
    ('双肩包,背包', 960),
    ('单肩包,挎包', 970),
    ('钱包,钱夹', 980),
    ('行李箱,旅行箱,拉杆箱', 990),
    ('腰带,皮带', 1000),
    ('手表,腕表', 1010),
    ('机械表,机械手表', 1020),
    ('石英表,石英手表', 1030),
    ('太阳镜,墨镜', 1040),
    ('项链,颈链', 1050),
    ('耳环,耳饰', 1060),
    ('戒指,指环', 1070),
    ('珠宝,首饰', 1080),
    ('家具,家私', 1090),
    ('台灯,桌灯', 1100),
    ('吊灯,悬吊灯', 1110),
    ('椅子,座椅,椅', 1120),
    ('沙发,沙发椅', 1130),
    ('床,床铺,床榻', 1140),
    ('床头柜,床边柜', 1150),
    ('衣柜,衣橱', 1160),
    ('餐桌,饭桌', 1170),
    ('茶几,茶桌', 1180),
    ('书桌,写字台', 1190),
    ('书架,书柜', 1200),
    ('床垫,睡垫', 1210),
    ('被子,被褥', 1220),
    ('枕头,枕', 1230),
    ('床单,褥单,被单', 1240),
    ('收纳箱,储物箱', 1250),
    ('衣架,挂衣架', 1260),
    ('垃圾桶,废物桶', 1270),
    ('雨伞,伞', 1280),
    ('保温杯,保温水杯', 1290),
    ('水杯,杯子', 1300),
    ('炒锅,炒菜锅', 1310),
    ('餐具,食具', 1320),
    ('菜刀,厨刀', 1330),
    ('砧板,菜板', 1340),
    ('瑜伽垫,健身垫', 1350),
    ('自行车,单车,脚踏车', 1360),
    ('电动车,电动自行车', 1370),
    ('头盔,安全帽', 1380),
    ('帐篷,露营帐篷', 1390),
    ('睡袋,露营睡袋', 1400),
    ('鱼竿,钓鱼竿', 1410),
    ('羽毛球拍,羽拍', 1420),
    ('篮球,蓝球', 1430),
    ('轮胎,车胎', 1440),
    ('机油,润滑油', 1450),
    ('行车记录仪,记录仪', 1460),
    ('车载充电器,车充', 1470),
    ('零食,零嘴', 1480),
    ('坚果,干果', 1490),
    ('饼干,饼乾', 1500),
    ('巧克力,朱古力', 1510),
    ('咖啡,coffee', 1520),
    ('茶叶,茶', 1530),
    ('牛奶,鲜奶', 1540),
    ('酸奶,酸乳', 1550),
    ('矿泉水,饮用水,纯净水', 1560),
    ('果汁,果汁饮料', 1570),
    ('方便面,泡面,速食面', 1580),
    ('大米,米', 1590),
    ('食用油,食油', 1600),
    ('蜂蜜,蜜糖', 1610),
    ('冰淇淋,冰激凌,雪糕', 1620),
    ('母婴,母婴用品', 1630),
    ('纸尿裤,尿不湿', 1640),
    ('奶粉,配方奶粉', 1650),
    ('奶瓶,婴儿奶瓶', 1660),
    ('婴儿车,童车,推车', 1670),
    ('婴儿床,童床', 1680),
    ('玩具,玩物', 1690),
    ('积木,拼装积木', 1700),
    ('绘本,图画书', 1710),
    ('文具,学习用品', 1720),
    ('书包,学生包', 1730),
    ('橡皮,橡皮擦', 1740),
    ('尺子,直尺', 1750)
)
INSERT INTO search_synonym (
    id, terms, sort_order, rule_type, scope_type, scope_id,
    priority, status, remark, created_at
)
SELECT
    (EXTRACT(EPOCH FROM clock_timestamp()) * 1000000)::bigint
        + ROW_NUMBER() OVER (ORDER BY d.sort_order),
    d.terms,
    d.sort_order,
    1,
    1,
    0,
    d.sort_order,
    1,
    '内置商品通用同义词',
    now() AT TIME ZONE 'UTC'
FROM defaults d
WHERE NOT EXISTS (
    SELECT 1
    FROM search_synonym s
    WHERE s.terms = d.terms
      AND s.rule_type = 1
      AND s.scope_type = 1
      AND s.scope_id = 0
      AND s.is_deleted = false
);

COMMIT;
