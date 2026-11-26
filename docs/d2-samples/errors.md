# D2フェンス：エラー確認用サンプル

各フェンスは描画を拒否し、理由を表示することを期待します。
原文を編集できること、Markdown／docx出力ではコードとノート名_ページ名_error.txtが残ることを確認します。
example.invalidはエラー検査用の名前です。アプリはこれらをCLI起動前に拒否します。

## 1. importは禁止

```d2
...@external
```

## 2. HTTP画像は禁止

```d2
端末: {icon: "http://example.invalid/icon.png"}
```

## 3. HTTPS画像は禁止

```d2
端末: {shape: image; icon: "https://example.invalid/image.jpg"}
```

## 4. 相対パスは禁止

```d2
端末: {icon: "assets/sample.png"}
```

## 5. SVGは禁止

```d2
端末: {icon: "/absolute/path/sample.svg"}
```

## 6. Layersは非対応

```d2
本体: 全体図
layers: {詳細: {A -> B}}
```

## 7. Scenariosは非対応

```d2
本体: 基本の図
scenarios: {別条件: {A -> B}}
```

## 8. Stepsは非対応

```d2
本体: 最初の図
steps: {次段階: {A -> B}}
```

## 9. クラス経由のURL指定も禁止

```d2
classes: {端末: {icon: "https://example.invalid/icon.png"}}
PC: {class: 端末}
```

## 10. Markdown内の画像も非対応

```d2
説明: |md
  ![外部画像](https://example.invalid/image.png)
|
```

## 11. 変数展開は初版では非対応

```d2
vars: {名前: 図}
説明: "${名前}"
```

## 12. D2の構文エラー

この例は参照制限の検査を通過した後、D2自身の構文診断が表示されます。

```d2
図: {
```

## 13. 正常な図への復帰

エラーのある図と同じページでも、この図は描画できます。

```d2
修正 -> 再描画 -> 成功
```
