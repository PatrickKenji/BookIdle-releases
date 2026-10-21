"""Gera a marca do BookIdle (livro em pixel art + letreiro) e o ícone do app.

Uso (da raiz do repositório):  python tools/brand/gen_brand.py
Saída em src/IdleDeck.App/Resources: LogoBookIdle.png, LogoBookIdleMini.png, AppIcon.ico
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
RES = ROOT / "src" / "IdleDeck.App" / "Resources"
FONT = RES / "Fonts" / "PressStart2P-Regular.ttf"

# O contorno, C capa, S lombada, P página, L linha de texto, Y marcador, * brilho
W, H = 22, 16


def build_book() -> list[str]:
    g = [["." for _ in range(W)] for _ in range(H)]

    def box(x0, y0, x1, y1, border, fill):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                edge = x in (x0, x1) or y in (y0, y1)
                g[y][x] = border if edge else fill

    box(0, 6, 21, 15, "O", "C")            # capa
    box(2, 4, 10, 13, "O", "P")            # página esquerda
    box(11, 4, 19, 13, "O", "P")           # página direita
    for y in range(5, 13):                  # lombada no meio
        g[y][10] = g[y][11] = "S"
    for y in (7, 9, 11):                    # linhas de texto
        for x in range(4, 9):
            g[y][x] = "L"
        for x in range(13, 18 if y != 11 else 16):
            g[y][x] = "L"
    for y in range(1, 9):                   # marcador pendurado
        g[y][15] = g[y][18] = "O"
        g[y][16] = g[y][17] = "Y"
    g[8][16] = g[8][17] = "O"
    g[1][15] = g[1][16] = g[1][17] = g[1][18] = "O"
    for cx, cy in ((1, 2), (20, 2)):       # brilhos em cruz
        for dx, dy in ((0, 0), (-1, 0), (1, 0), (0, -1), (0, 1)):
            g[cy + dy][cx + dx] = "*"
    return ["".join(r) for r in g]


BOOK = build_book()
COLORS = {
    "O": (0x12, 0x0A, 0x22, 255),
    "C": (0x7B, 0x2F, 0xD0, 255),
    "S": (0x5A, 0x1F, 0xA0, 255),
    "P": (0xEB, 0xDD, 0xFF, 255),
    "L": (0xB9, 0x96, 0xF0, 255),
    "Y": (0xFF, 0xD2, 0x5A, 255),
    "*": (0xFF, 0xD2, 0x5A, 255),
}
YELLOW = (0xFF, 0xD2, 0x5A, 255)
LILAC = (0xE0, 0xB8, 0xFF, 255)
SHADOW_Y = (0x7A, 0x2E, 0x00, 255)
SHADOW_L = (0x4A, 0x1C, 0x9E, 255)


def draw_book(img: Image.Image, x: int, y: int, cell: int) -> None:
    d = ImageDraw.Draw(img)
    for row, line in enumerate(BOOK):
        for col, ch in enumerate(line):
            if ch in COLORS:
                d.rectangle(
                    [x + col * cell, y + row * cell, x + (col + 1) * cell - 1, y + (row + 1) * cell - 1],
                    fill=COLORS[ch],
                )


def text_with_shadow(img: Image.Image, xy, text, font, fill, shadow, offset) -> None:
    d = ImageDraw.Draw(img)
    d.fontmode = "1"  # sem antialias: pixel nítido
    x, y = xy
    d.text((x + offset, y + offset), text, font=font, fill=shadow)
    d.text((x, y), text, font=font, fill=fill)


def logo_wide() -> Image.Image:
    cell = 11
    width, height = 576, 240
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    book_w, book_h = len(BOOK[0]) * cell, len(BOOK) * cell
    draw_book(img, 10, (height - book_h) // 2, cell)
    font = ImageFont.truetype(str(FONT), 64)
    x = 10 + book_w + 28
    text_with_shadow(img, (x, 46), "BOOK", font, YELLOW, SHADOW_Y, 6)
    text_with_shadow(img, (x, 130), "IDLE", font, LILAC, SHADOW_L, 6)
    return img


def logo_mini() -> Image.Image:
    cell = 8  # 22x16 células → 176x128, mostrado a 44x32
    img = Image.new("RGBA", (W * cell, H * cell), (0, 0, 0, 0))
    draw_book(img, 0, 0, cell)
    return img


def app_icon() -> Image.Image:
    size = 256
    cell = 12
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    # fundo de selo roxo escuro, para o ícone não sumir em barras claras
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([8, 8, size - 9, size - 9], radius=48, fill=(0x24, 0x14, 0x3F, 255), outline=COLORS["O"], width=8)
    book_w, book_h = len(BOOK[0]) * cell, len(BOOK) * cell
    draw_book(img, (size - book_w) // 2, (size - book_h) // 2, cell)
    return img


def main() -> None:
    logo_wide().save(RES / "LogoBookIdle.png")
    logo_mini().save(RES / "LogoBookIdleMini.png")
    icon = app_icon()
    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)]
    # Pillow reduz a partir da imagem base; NEAREST mantém o pixel duro
    frames = [icon.resize(s, Image.NEAREST) for s in sizes]
    frames[-1].save(RES / "AppIcon.ico", format="ICO", sizes=sizes, append_images=frames[:-1])
    print("ok:", RES)


if __name__ == "__main__":
    main()
