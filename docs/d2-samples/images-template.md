# D2フェンス：ローカル画像のサンプル

`__PNG_ABSOLUTE_PATH__`と`__JPG_ABSOLUTE_PATH__`を、同梱assets内の画像の絶対パスへ置き換えてください。
WindowsではC:/Users/example/...のようにスラッシュを使うと、そのまま引用符内へ指定できます。
画像ファイルはこのサンプル用に生成した単純な色帯です。

## 1. PNGとJPEGのアイコン

```d2
direction: right
PNG: {icon: "__PNG_ABSOLUTE_PATH__"}
JPEG: {icon: "__JPG_ABSOLUTE_PATH__"}
PNG -> JPEG: ローカル画像
```

## 2. 画像そのものを表示

```d2
画像: {
  shape: image
  icon: "__PNG_ABSOLUTE_PATH__"
}
```

## 3. 同じ画像を複数の図で使用

```d2
再利用: {icon: "__PNG_ABSOLUTE_PATH__"}
```
