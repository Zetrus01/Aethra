
// Navbar komponens kezelése - DROPDOWN BEJELENTKEZÉSSEL
class NavbarComponent {
    constructor() {
        this.navbarLoaded = false;
        this.initComplete = false;
        this.authUIInitialized = false;
        this.pageVisible = true;
        this.init();
        
        // Figyeljük a visibility változást
        this.initVisibilityHandler();
    }

    // Visibility változás kezelése
    initVisibilityHandler() {
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                this.pageVisible = false;
                console.log('📱 Oldal elrejtve');
            } else {
                this.pageVisible = true;
                console.log('📱 Oldal vissza, dropdownok újrainicializálása...');
                
                // Kis késleltetéssel újrainicializáljuk a dropdownokat
                setTimeout(() => {
                    this.reinitializeDropdowns();
                }, 100);
            }
        });

        // Window focus esemény
        window.addEventListener('focus', () => {
            console.log('📱 Window focus, dropdownok újrainicializálása...');
            setTimeout(() => {
                this.reinitializeDropdowns();
            }, 100);
        });
    }

    // Dropdownok újrainicializálása
    reinitializeDropdowns() {
        // Bootstrap dropdownok újrainicializálása
        $('.profile-dropdown .profile-btn').each(function() {
            // Eltávolítjuk a régi dropdown adatot
            $(this).removeData('bs.dropdown');
            // Újrainicializáljuk
            $(this).dropdown();
        });

        // Ha van authManager, frissítjük az UI-t
        if (window.authManager) {
            if (authManager.isAuthenticated) {
                this.updateAuthUI(true, authManager.userData?.userName);
            } else {
                this.updateAuthUI(false);
            }
        }

        console.log('✅ Dropdownok újrainicializálva');
    }

    async init() {
        console.log('🚀 NavbarComponent inicializálás...');
        
        // 1. Téma betöltése
        this.initTheme();
        
        // 2. Navbar betöltése és várakozás a teljes betöltésre
        await this.loadNavbar();
        
        // 3. Funkciók inicializálása
        this.initNavbarFunctionality();
        
        // 4. Jelzés, hogy a navbar készen áll
        this.initComplete = true;
        
        // 5. Esemény küldése, hogy a navbar betöltődött
        this.emitNavbarLoaded();
    }

    // Téma kezelés inicializálása
    initTheme() {
        const savedTheme = localStorage.getItem('theme');
        
        if (!savedTheme) {
            document.documentElement.setAttribute('data-theme', 'dark');
            localStorage.setItem('theme', 'dark');
            console.log('🎨 Alapértelmezett sötét téma beállítva');
        } else {
            document.documentElement.setAttribute('data-theme', savedTheme);
            console.log(`🎨 Téma betöltve a localStorage-ból: ${savedTheme}`);
        }
    }

    // Navbar betöltése - ASZINKRON VÁLTOZAT
    loadNavbar() {
        return new Promise((resolve) => {
            if (this.navbarLoaded) {
                resolve();
                return;
            }

            console.log('⏳ Navbar betöltése...');
            
            $.ajax({
                url: "navbar.html",
                type: "GET",
                dataType: "html",
                success: (data) => {
                    $("#navbar-container").html(data);
                    this.navbarLoaded = true;
                    console.log("✅ Navbar HTML betöltve");
                    
                    // Kis késleltetés a DOM frissítéséhez
                    setTimeout(() => {
                        resolve();
                    }, 100);
                },
                error: (xhr, status, error) => {
                    console.error("❌ Navbar betöltési hiba:", error);
                    this.loadFallbackNavbar();
                    
                    // Fallback esetén is jelzünk
                    setTimeout(() => {
                        resolve();
                    }, 100);
                }
            });
        });
    }

    // Navbar funkciók inicializálása
    initNavbarFunctionality() {
        // Scroll effekt
        this.initScrollEffect();
        
        // Aktív menüpont beállítása
        this.setActiveMenuItem();
        
        // Mobil menü kezelése
        this.initMobileMenu();
        
        // Téma váltó inicializálása
        this.initThemeToggle();
        
        // Kattintás események
        this.bindClickEvents();
        
        console.log("✅ Navbar funkciók inicializálva");
    }

    // Kattintás események kötése
    bindClickEvents() {
        setTimeout(() => {
            // BEJELENTKEZÉS GOMB (ha még sima gombként létezik)
            const loginBtn = document.getElementById('login-button');
            if (loginBtn) {
                console.log('🔗 Bejelentkezés gomb esemény kötése...');
                $(loginBtn).off('click').on('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    console.log('👉 Bejelentkezés gombra kattintottak');
                    window.location.href = 'login.html';
                });
            }
            
            // KIjelentkezés gomb
            const logoutBtn = document.getElementById('logout-button');
            if (logoutBtn) {
                $(logoutBtn).off('click').on('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    console.log('👉 Kijelentkezés gombra kattintottak');
                    if (window.authManager && typeof window.authManager.logout === 'function') {
                        window.authManager.logout();
                    }
                });
            }
            
            // Kosár gomb
            const cartBtn = document.getElementById('cart-button');
            if (cartBtn) {
                $(cartBtn).off('click').on('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    window.location.href = 'cart.html';
                });
            }
            
            // Bejelentkezés dropdown linkek
            $('#login-dropdown-link').off('click').on('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                window.location.href = 'login.html';
            });
            
            $('#register-dropdown-link').off('click').on('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                window.location.href = 'signup.html';
            });
        }, 200);
    }

    // Scroll effekt
    initScrollEffect() {
        $(window).on("scroll", () => {
            if ($(window).scrollTop() > 50) {
                $(".navbar-custom").addClass("scrolled");
            } else {
                $(".navbar-custom").removeClass("scrolled");
            }
        });

        if ($(window).scrollTop() > 50) {
            $(".navbar-custom").addClass("scrolled");
        }
    }

    // Aktív menüpont beállítása
    setActiveMenuItem() {
        const currentPage = window.location.pathname.split('/').pop() || 'index.html';
        
        $('.navbar-nav .nav-link').removeClass('active');
        
        $('.navbar-nav .nav-link').each(function() {
            const linkPage = $(this).attr('href');
            if (linkPage === currentPage) {
                $(this).addClass('active');
            }
        });
    }

    // Mobil menü kezelése
    initMobileMenu() {
        setTimeout(() => {
            this.setupMobileMenuEvents();
        }, 100);
    }

    setupMobileMenuEvents() {
        const $navbarCollapse = $('#navbarNav');
        const $navbarToggler = $('.navbar-toggler');

        console.log('🔄 Mobil menü események beállítása...');

        $navbarToggler.off('click.mobile').on('click.mobile', (e) => {
            e.preventDefault();
            e.stopPropagation();
            
            const isCurrentlyExpanded = $navbarToggler.attr('aria-expanded') === 'true';
            const willBeExpanded = !isCurrentlyExpanded;
            
            if (willBeExpanded) {
                this.openMobileMenu();
            } else {
                this.closeMobileMenu();
            }
        });

        $(document).off('click.mobile').on('click.mobile', '.navbar-nav .nav-link, .dropdown-item, #login-dropdown-link, #register-dropdown-link', (e) => {
            if ($(window).width() < 992 && $navbarCollapse.hasClass('show')) {
                this.closeMobileMenu();
            }
        });

        console.log('✅ Mobil menü események beállítva');
    }

    closeMobileMenu() {
        const $navbarCollapse = $('#navbarNav');
        const $navbarToggler = $('.navbar-toggler');
        
        $navbarCollapse.removeClass('show');
        $navbarToggler.attr('aria-expanded', 'false');
        $navbarCollapse.collapse('hide');
    }

    openMobileMenu() {
        const $navbarCollapse = $('#navbarNav');
        const $navbarToggler = $('.navbar-toggler');
        
        $navbarCollapse.addClass('show');
        $navbarToggler.attr('aria-expanded', 'true');
        $navbarCollapse.collapse('show');
    }

    // Fallback navbar
    loadFallbackNavbar() {
        const fallbackNavbar = `
            <nav class="navbar navbar-expand-lg navbar-dark navbar-custom">
                <div class="container">
                    <a class="navbar-brand" href="index.html">AETHRA</a>
                    <button class="navbar-toggler" type="button" data-toggle="collapse" data-target="#navbarNav" aria-expanded="false" aria-controls="navbarNav">
                        <span class="navbar-toggler-icon"></span>
                    </button>
                    <div class="collapse navbar-collapse" id="navbarNav">
                        <ul class="navbar-nav ml-auto">
                            <li class="nav-item"><a class="nav-link" href="index.html">Főoldal</a></li>
                            <li class="nav-item"><a class="nav-link" href="reserve.html">Asztalfoglalás</a></li>
                            <li class="nav-item"><a class="nav-link" href="menu.html">Menü</a></li>
                            <li class="nav-item"><a class="nav-link" href="contact.html">Kapcsolat</a></li>
                        </ul>
                        <div id="login-logout-container" class="ml-3">
                            <!-- Ide kerül dinamikusan a bejelentkezési dropdown vagy a profil dropdown -->
                        </div>
                    </div>
                </div>
            </nav>
        `;
        
        $("#navbar-container").html(fallbackNavbar);
        this.navbarLoaded = true;
        
        setTimeout(() => {
            this.initNavbarFunctionality();
        }, 50);
        
        console.log("✅ Fallback navbar betöltve");
    }

    // Bejelentkezési UI frissítése
    updateAuthUI(isLoggedIn, userName = null) {
        this.authUIInitialized = true;
        
        setTimeout(() => {
            const container = $('#login-logout-container');
            
            if (!container.length) {
                console.warn("⚠️ login-logout-container nem található, 2. próbálkozás...");
                
                setTimeout(() => {
                    this.updateAuthUI(isLoggedIn, userName);
                }, 200);
                return;
            }

            if (isLoggedIn && userName) {
                container.html(this.getLoggedInTemplate(userName));
                this.initDropdownInteractions();
                this.updateThemeButton();
                console.log(`✅ Bejelentkezett UI megjelenítve: ${userName}`);
            } else {
                container.html(this.getLoggedOutTemplate());
                console.log(`✅ Kijelentkezett UI megjelenítve (dropdown bejelentkezés)`);
                
                // Bootstrap dropdown inicializálása
                this.initLoggedOutDropdown();
                
                // Biztonsági inicializálás többször
                setTimeout(() => {
                    this.initLoggedOutDropdown();
                }, 200);
                
                // Újra kötjük a bejelentkezési linkek eseményeit
                this.bindClickEvents();
            }
            
            this.emitAuthUIUpdated(isLoggedIn, userName);
        }, 100);
    }

    // Bejelentkezett felhasználó template
    getLoggedInTemplate(userName) {
        const currentTheme = document.documentElement.getAttribute('data-theme') || 'dark';
        const themeText = currentTheme === 'dark' ? 'Világos téma' : 'Sötét téma';
        const themeIcon = currentTheme === 'dark' ? 'bi-sun' : 'bi-moon';
        
        return `
            <div class="dropdown profile-dropdown">
                <button class="btn btn-outline-light btn-sm profile-btn" type="button" id="profileDropdown" data-toggle="dropdown" aria-haspopup="true" aria-expanded="false">
                    <i class="bi bi-person"></i>
                </button>
                <div class="dropdown-menu dropdown-menu-right bg-dark text-light shadow-lg border-0 mt-2" aria-labelledby="profileDropdown">
                    <div class="dropdown-header text-warning">
                        <small>Bejelentkezve mint</small><br>
                        <strong>${userName}</strong>
                    </div>
                    <div class="dropdown-divider"></div>
                    <a class="dropdown-item text-light" href="profile.html">
                        <i class="bi bi-person"></i> Profilom
                    </a>
                    <a class="dropdown-item text-light" href="cart.html">
                        <i class="bi bi-bag"></i> Kosár
                    </a>
                    <button id="theme-toggle-button" class="dropdown-item text-light">
                        <i class="bi ${themeIcon}"></i> ${themeText}
                    </button>
                    <div class="dropdown-divider"></div>
                    <button id="logout-button" class="dropdown-item text-danger">
                        <i class="bi bi-box-arrow-right"></i> Kijelentkezés
                    </button>
                </div>
            </div>
        `;
    }

    // Kijelentkezett felhasználó template
    getLoggedOutTemplate() {
        const currentTheme = document.documentElement.getAttribute('data-theme') || 'dark';
        const themeText = currentTheme === 'dark' ? 'Világos téma' : 'Sötét téma';
        const themeIcon = currentTheme === 'dark' ? 'bi-sun' : 'bi-moon';
        
        return `
            <div class="dropdown profile-dropdown">
                <button class="btn btn-outline-light btn-sm profile-btn" type="button" id="loginDropdown" data-toggle="dropdown" aria-haspopup="true" aria-expanded="false">
                    <i class="bi bi-box-arrow-in-right" style="transform: translate(-1px, 0);"></i>
                </button>
                <div class="dropdown-menu dropdown-menu-right shadow-lg border-0 mt-2" aria-labelledby="loginDropdown">
                    <div class="dropdown-header text-center text-warning">
                        <i class="bi bi-person-circle" style="font-size: 2rem;"></i><br>
                        <small>Üdvözöljük!</small>
                    </div>
                    <div class="dropdown-divider"></div>
                    <a class="dropdown-item" href="#" id="login-dropdown-link">
                        <i class="bi bi-box-arrow-in-right"></i> Bejelentkezés
                    </a>
                    <a class="dropdown-item" href="signup.html" id="register-dropdown-link">
                        <i class="bi bi-person-plus"></i> Regisztráció
                    </a>
                    <button id="theme-toggle-button" class="dropdown-item">
                        <i class="bi ${themeIcon}"></i> ${themeText}
                    </button>
                    <div class="dropdown-divider"></div>
                    <a class="dropdown-item" href="cart.html" id="login-dropdown-link">
                        <i class="bi bi-bag"></i> Kosár
                    </a>
                </div>
            </div>
        `;
    }

    // Nem bejelentkezett dropdown inicializálása
    initLoggedOutDropdown() {
        // Először eltávolítjuk a régi dropdownokat
        $('.profile-dropdown .profile-btn').each(function() {
            $(this).removeData('bs.dropdown');
        });

        // Bootstrap dropdown inicializálása
        setTimeout(() => {
            $('.profile-dropdown .profile-btn').dropdown();
            
            // Ellenőrizzük, hogy működik-e
            const dropdownBtn = document.getElementById('loginDropdown');
            if (dropdownBtn && !$(dropdownBtn).data('bs.dropdown')) {
                $(dropdownBtn).dropdown();
            }
        }, 50);
        
        // Téma váltás inicializálása
        this.initThemeToggle();
        
        // Bejelentkezés link eseménykezelő
        $('#login-dropdown-link').off('click').on('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            window.location.href = 'login.html';
        });
        
        // Regisztráció link eseménykezelő
        $('#register-dropdown-link').off('click').on('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            window.location.href = 'register.html';
        });
        
        console.log('✅ Nem bejelentkezett dropdown inicializálva');
    }

    // Dropdown interakciók inicializálása (bejelentkezett állapothoz)
    initDropdownInteractions() {
        // Bootstrap dropdown
        $('.profile-dropdown .profile-btn').dropdown();
        
        // Téma váltás
        this.initThemeToggle();
        
        // Kijelentkezés gomb eseménye
        const logoutBtn = document.getElementById('logout-button');
        if (logoutBtn) {
            $(logoutBtn).off('click').on('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                if (window.authManager && typeof window.authManager.logout === 'function') {
                    window.authManager.logout();
                }
            });
        }
    }

    // Téma váltás kezelése
    initThemeToggle() {
        $(document).off('click', '#theme-toggle-button').on('click', '#theme-toggle-button', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.toggleTheme();
        });
    }

    // Téma váltás logika
    toggleTheme() {
        const currentTheme = document.documentElement.getAttribute('data-theme') || 'dark';
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        
        document.documentElement.setAttribute('data-theme', newTheme);
        localStorage.setItem('theme', newTheme);
        
        console.log(`🎨 Téma váltás: ${currentTheme} → ${newTheme}`);
        
        this.updateThemeButton();
        
        // UI frissítése
        if (window.authManager) {
            if (authManager.isAuthenticated) {
                this.updateAuthUI(true, authManager.userData?.userName);
            } else {
                this.updateAuthUI(false);
            }
        }
    }

    // Téma gomb frissítése
    updateThemeButton() {
        const currentTheme = document.documentElement.getAttribute('data-theme') || 'dark';
        const themeButton = $('#theme-toggle-button');
        
        if (themeButton.length) {
            const themeText = currentTheme === 'dark' ? 'Világos téma' : 'Sötét téma';
            const themeIcon = currentTheme === 'dark' ? 'bi-sun' : 'bi-moon';
            
            themeButton.html(`<i class="bi ${themeIcon}"></i> ${themeText}`);
        }
    }

    // Események küldése
    emitNavbarLoaded() {
        setTimeout(() => {
            console.log('🚀 Navbar teljesen betöltve, esemény küldése...');
            
            window.navbarLoaded = true;
            
            const event = new CustomEvent('navbarLoaded', {
                detail: { timestamp: new Date() }
            });
            document.dispatchEvent(event);
            
            console.log('✅ NavbarLoaded esemény elküldve');
        }, 300);
    }

    emitAuthUIUpdated(isLoggedIn, userName) {
        const event = new CustomEvent('authUIUpdated', {
            detail: { 
                isLoggedIn, 
                userName,
                timestamp: new Date() 
            }
        });
        document.dispatchEvent(event);
    }
}

// Globális instance
const navbar = new NavbarComponent();

// Globális elérhetőség
window.NavbarComponent = NavbarComponent;
window.navbar = navbar;

// Oldal betöltése után
$(document).ready(function() {
    console.log('📄 Dokumentum betöltve, további inicializálások...');
    
    // Eseményfigyelő a navbar betöltésére
    document.addEventListener('navbarLoaded', function(e) {
        console.log('🎯 Navbar betöltés esemény fogadva:', e.detail);
        
        if (window.authManager && typeof window.authManager.updateUI === 'function') {
            console.log('🔄 Azonnali UI frissítés AuthManager-rel');
            setTimeout(() => {
                authManager.updateUI();
            }, 100);
        }
    });
});
