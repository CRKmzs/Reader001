# -*- coding: utf-8 -*-
"""生成各种编码的 TXT 样本，供 tests/encoding.test.mjs 校验解码正确性。
用法: python tests/encoding-samples.py
输出: tests/encoding-samples/<name>.bin 与 <name>.expected.txt (UTF-8, LF)
"""
import os

BASE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(BASE, "encoding-samples")

PARA = "第一章 夜航船\n雨是在子时停的。陈砚把最后一盏灯笼吹熄，江面上就只剩下水声。\n他翻过一页，指尖停在一行小字上：星移斗转，其下必有变。\n\n"
SHORT = "第一章 少年归来\n他在山门口站了很久，雪落在肩上。\n「走吧。」他说。\n"
GBK_TEXT = "第一章 少年归来\n他在山门口站了很久，雪落在肩上。\n「走吧。」他说。\n"
BIG5_TEXT = "第一章 少年歸來\n他在山門口站了很久，雪落在肩上。\n「走吧。」他說。\n"
GB18030_TEXT = "第一章 生僻字\n㐀㐁㐂 龦龧 犇骉 汉字测试。\n"
CJK_PUNCT = "第一章 标点\n他说：「今天的雨，真大。」——她笑了……（完）\n"

long_utf8 = ""
while len(long_utf8.encode("utf-8")) < 1250 * 1024:
    long_utf8 += PARA
long_gbk = ""
while len(long_gbk.encode("gbk")) < 1250 * 1024:
    long_gbk += GBK_TEXT

cases = [
    ("utf8", SHORT, "utf-8"),
    ("utf8_bom", SHORT, "utf-8-sig"),
    ("utf8_punct", CJK_PUNCT, "utf-8"),
    ("utf8_long", long_utf8, "utf-8"),
    ("gbk", GBK_TEXT, "gbk"),
    ("gbk_long", long_gbk, "gbk"),
    ("gb18030", GB18030_TEXT, "gb18030"),
    ("big5", BIG5_TEXT, "big5"),
    ("utf16le", SHORT, "utf-16-le"),
    ("utf16be", SHORT, "utf-16-be"),
    ("utf16le_bom", SHORT, "utf-16"),
]

def main():
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    for name, text, enc in cases:
        data = text.encode(enc)
        if name == "utf16be":
            data = b"\xfe\xff" + text.encode("utf-16-be")  # 无 BOM 的写出（下面覆盖）
            data = text.encode("utf-16-be")
        with open(os.path.join(OUT, name + ".bin"), "wb") as f:
            f.write(data)
        with open(os.path.join(OUT, name + ".expected.txt"), "wb") as f:
            f.write(text.encode("utf-8"))
        print("wrote", name, len(data), "bytes")

    # 单字节损坏的 UTF-8：验证不会因此整篇退化为 GB18030
    raw = bytearray(SHORT.encode("utf-8"))
    raw[30] = 0xFF
    with open(os.path.join(OUT, "utf8_one_bad_byte.bin"), "wb") as f:
        f.write(bytes(raw))
    print("wrote utf8_one_bad_byte", len(raw), "bytes")

if __name__ == "__main__":
    main()
