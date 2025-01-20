// このファイルはウェブサイトのインタラクティブな機能を実装するJavaScriptファイルです。
// DOM操作やイベントリスナーが含まれます。

document.addEventListener('DOMContentLoaded', function() {
    // ここに初期化コードを追加します
    console.log('ウェブサイトが読み込まれました');

    // 例: ボタンのクリックイベントリスナーを追加
    const button = document.getElementById('myButton');
    if (button) {
        button.addEventListener('click', function() {
            alert('ボタンがクリックされました！');
        });
    }
});