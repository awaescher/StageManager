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