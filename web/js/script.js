document.addEventListener('DOMContentLoaded', () => {
    const image = document.getElementById('animatedImage');

    // 初期位置とアニメーションの設定
    let position = 0;
    const speed = 2; // アニメーションの速度

    function animate() {
        position += speed;
        image.style.transform = `translateX(${position}px)`;

        // 画面の端に到達したら反対方向に移動
        if (position > window.innerWidth - image.width || position < 0) {
            speed *= -1;
        }

        requestAnimationFrame(animate);
    }

    animate();
});

// モバイルメニューの制御
const menuButton = document.querySelector('.mobile-menu-button');
const navLinks = document.querySelector('.nav-links');

menuButton.addEventListener('click', () => {
    navLinks.classList.toggle('active');
});

// メニューリンクをクリックしたらメニューを閉じる
document.querySelectorAll('.nav-links a').forEach(link => {
    link.addEventListener('click', () => {
        navLinks.classList.remove('active');
    });
});