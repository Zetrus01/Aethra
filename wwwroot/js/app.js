// js/app.js - TELJESEN JAVÍTOTT ÉS BIZTONSÁGOS VERZIÓ
class App {
    constructor() {
        console.log('🚀 Alkalmazás inicializálása...');
        this.isInitialized = false;
        this.initializationPhase = 'not_started';
        this.dependencies = {
            navbar: false,
            authManager: false,
            cartManager: false
        };
        
        // Bind metódusok, hogy megmaradjon a this kontextus
        this.onCartManagerReady = this.onCartManagerReady.bind(this);
        this.onCartUpdated = this.onCartUpdated.bind(this);
        this.onReservationUpdated = this.onReservationUpdated.bind(this);
        this.onAuthStateChanged = this.onAuthStateChanged.bind(this);
    }

    async init() {
        try {
            console.log('📄 Oldal specifikus inicializálás');
            console.time('App initialization');
            this.initializationPhase = 'starting';
            
            // 1. Várjuk meg a Navbar betöltését (legfontosabb)
            console.log('⏳ Navbar betöltésének várakozása...');
            await this.waitForNavbar();
            
            // 2. Várjuk meg az AuthManager betöltését
            console.log('⏳ AuthManager betöltésének várakozása...');
            await this.waitForAuthManager();
            
            // 3. Várjuk meg a CartManager betöltését
            console.log('⏳ CartManager betöltésének várakozása...');
            await this.waitForCartManager();
            
            // 4. Eseménykezelők beállítása
            this.setupEventListeners();
            
            // 5. Oldal specifikus inicializálás
            console.log('⏳ Oldal specifikus inicializálás...');
            await this.initPageSpecific();
            
            this.isInitialized = true;
            this.initializationPhase = 'completed';
            console.timeEnd('App initialization');
            console.log('✅ Alkalmazás sikeresen inicializálva');
            
            // Frissítsük az UI-t
            this.refreshUI();
            
            // Esemény küldése, hogy az app készen áll
            window.dispatchEvent(new CustomEvent('appReady', { 
                detail: { 
                    timestamp: new Date(),
                    debugInfo: this.getDebugInfo()
                }
            }));
            
        } catch (error) {
            console.error('❌ Hiba az alkalmazás inicializálásakor:', error);
            this.initializationPhase = 'failed';
            
            // Küldjünk hibás eseményt
            window.dispatchEvent(new CustomEvent('appError', { 
                detail: { error, timestamp: new Date() }
            }));
            
            // Mégis próbáljuk meg frissíteni az UI-t
            setTimeout(() => this.refreshUI(), 1000);
        }
    }
    
    // Navbar betöltésének várakozása - javított változat
    waitForNavbar() {
        return new Promise((resolve) => {
            // Többféle módon ellenőrizzük a Navbar jelenlétét
            const navbarCheck = () => {
                const navbarExists = 
                    (window.navbar && window.navbar.navbarLoaded) ||
                    (window.NavbarComponent && typeof window.NavbarComponent.load === 'function') ||
                    (window.components && window.components.navbar) ||
                    document.getElementById('navbar-container') ||
                    document.querySelector('nav') ||
                    document.querySelector('.navbar') ||
                    document.querySelector('[data-navbar]');
                
                return navbarExists;
            };
            
            if (navbarCheck()) {
                console.log('✅ Navbar már elérhető');
                this.dependencies.navbar = true;
                resolve();
                return;
            }
            
            const maxAttempts = 50; // Több próbálkozás
            let attempts = 0;
            
            const checkNavbar = () => {
                attempts++;
                
                if (navbarCheck()) {
                    console.log(`✅ Navbar betöltve (${attempts} próbálkozás után)`);
                    this.dependencies.navbar = true;
                    resolve();
                } else if (attempts >= maxAttempts) {
                    console.warn(`⚠️ Navbar nem lett betöltve ${maxAttempts} próbálkozás után`);
                    console.log('ℹ️ Folytatjuk a Navbar nélkül - próbáljuk meg később betölteni');
                    // Próbáljuk meg később betölteni
                    setTimeout(() => {
                        if (navbarCheck()) {
                            console.log('✅ Navbar később betöltve');
                            this.dependencies.navbar = true;
                        }
                    }, 2000);
                    resolve();
                } else {
                    setTimeout(checkNavbar, 100);
                }
            };
            
            checkNavbar();
        });
    }
    
    // AuthManager betöltésének várakozása - javított változat
    waitForAuthManager() {
        return new Promise((resolve) => {
            const authCheck = () => {
                return window.authManager || 
                       window.AuthManager || 
                       typeof window.AuthManager !== 'undefined';
            };
            
            if (authCheck()) {
                console.log('✅ AuthManager már elérhető');
                this.dependencies.authManager = true;
                resolve();
                return;
            }
            
            const maxAttempts = 50;
            let attempts = 0;
            
            const checkAuthManager = () => {
                attempts++;
                
                if (authCheck()) {
                    console.log(`✅ AuthManager betöltve (${attempts} próbálkozás után)`);
                    this.dependencies.authManager = true;
                    resolve();
                } else if (attempts >= maxAttempts) {
                    console.warn(`⚠️ AuthManager nem lett betöltve ${maxAttempts} próbálkozás után`);
                    console.log('ℹ️ Folytatjuk az AuthManager nélkül');
                    resolve();
                } else {
                    setTimeout(checkAuthManager, 100);
                }
            };
            
            checkAuthManager();
        });
    }
    
    // CartManager betöltésének várakozása - javított változat
    waitForCartManager() {
        return new Promise((resolve) => {
            const cartCheck = () => {
                return (window.cartManager && window.cartManager.isInitialized) ||
                       (window.SmartCartManager && window.SmartCartManager.prototype);
            };
            
            if (cartCheck()) {
                console.log('✅ CartManager már inicializálva');
                this.dependencies.cartManager = true;
                resolve();
                return;
            }
            
            const maxAttempts = 60; // Több idő a kosárkezelőnek
            let attempts = 0;
            
            const checkCartManager = () => {
                attempts++;
                
                if (cartCheck()) {
                    console.log(`✅ CartManager inicializálva (${attempts} próbálkozás után)`);
                    this.dependencies.cartManager = true;
                    resolve();
                } else if (attempts >= maxAttempts) {
                    console.warn(`⚠️ CartManager nem inicializálódott ${maxAttempts} próbálkozás után`);
                    
                    // Próbáljuk manuálisan létrehozni, ha a class létezik
                    if (window.SmartCartManager && !window.cartManager) {
                        try {
                            console.log('🔄 CartManager manuális példányosítás...');
                            window.cartManager = new window.SmartCartManager();
                            if (typeof window.cartManager.init === 'function') {
                                window.cartManager.init();
                            }
                            this.dependencies.cartManager = true;
                        } catch (e) {
                            console.error('❌ CartManager manuális inicializálás sikertelen:', e);
                        }
                    }
                    
                    console.log('ℹ️ Folytatjuk a CartManager nélkül');
                    resolve();
                } else {
                    setTimeout(checkCartManager, 100);
                }
            };
            
            checkCartManager();
        });
    }
    
    // Oldal specifikus inicializálás
    async initPageSpecific() {
        // Oldal típus alapján különböző inicializálás
        const path = window.location.pathname;
        const page = path.split('/').pop() || 'index.html';
        
        console.log(`📄 Oldal: ${page}`);
        
        if (page.includes('cart.html') || path.includes('/cart')) {
            await this.initCartPage();
        } else if (page.includes('menu.html') || path.includes('/menu')) {
            await this.initMenuPage();
        } else if (page.includes('reservation.html') || path.includes('/reservation')) {
            await this.initReservationPage();
        } else if (page.includes('checkout.html') || path.includes('/checkout')) {
            await this.initCheckoutPage();
        } else if (page.includes('profile.html') || path.includes('/profile')) {
            await this.initProfilePage();
        } else if (page.includes('login.html') || path.includes('/login')) {
            await this.initLoginPage();
        } else if (page.includes('register.html') || path.includes('/register')) {
            await this.initRegisterPage();
        } else {
            // Főoldal vagy ismeretlen oldal
            await this.initHomePage();
        }
    }
    
    setupEventListeners() {
        console.log('🔗 Alap eseménykezelők beállítása');
        
        // CartManager események
        window.addEventListener('cartManagerReady', this.onCartManagerReady);
        window.addEventListener('cartUpdated', this.onCartUpdated);
        window.addEventListener('reservationUpdated', this.onReservationUpdated);
        
        // Auth események
        window.addEventListener('authStateChanged', this.onAuthStateChanged);
        window.addEventListener('userLoggedIn', () => this.onUserLoggedIn());
        window.addEventListener('userLoggedOut', () => this.onUserLoggedOut());
        
        // App események
        window.addEventListener('appRefresh', () => {
            console.log('🔄 App frissítés kérése');
            this.refreshUI();
        });
        
        // Online/offline állapot
        window.addEventListener('online', () => this.onOnlineStatusChanged(true));
        window.addEventListener('offline', () => this.onOnlineStatusChanged(false));
        
        // Visibility change (tab váltás)
        document.addEventListener('visibilitychange', () => {
            this.onVisibilityChange(document.hidden);
        });
    }
    
    onCartManagerReady() {
        console.log('🎯 CartManager elérhető, további inicializálások...');
        this.dependencies.cartManager = true;
        
        // Itt végezhetünk CartManager függő inicializálásokat
        if (window.cartManager) {
            try {
                const cartData = window.cartManager.getCartData ? window.cartManager.getCartData() : {};
                console.log('📊 Kosár adatok:', cartData);
                
                // Frissítsük a UI-t a kosár adatokkal
                this.updateCartUI(cartData);
            } catch (e) {
                console.warn('⚠️ Nem sikerült lekérni a kosár adatokat:', e);
            }
        }
    }
    
    onCartUpdated(detail) {
        // Kosár frissítés kezelése - BIZTONSÁGOS VERZIÓ
        try {
            console.log("🔄 Kosár frissítve (App):", detail);
            
            // Biztonságos adat kinyerés
            const itemCount = detail?.itemCount || 0;
            const totalItems = detail?.totalItems || 0;
            const cartData = detail?.cartData || detail || {};
            
            console.log(`🔄 Kosár frissítve: ${itemCount} elem, összesen: ${totalItems} db`);
            
            // Frissítsük a Navbar-t, ha van ilyen lehetőség
            this.updateCartCountUI(totalItems);
            
            // Frissítsük a localStorage-ban tárolt értéket
            localStorage.setItem('lastCartUpdate', new Date().toISOString());
            localStorage.setItem('cartItemCount', totalItems.toString());
            
            // Oldal specifikus frissítés
            this.updateCartUI({ totalItems, itemCount, cartData });
            
        } catch (error) {
            console.error("❌ Hiba a kosár frissítés kezelésekor:", error);
            // Alapértelmezett értékekkel folytatjuk
            this.updateCartCountUI(0);
        }
    }
    
    onReservationUpdated(detail) {
        // Foglalás frissítés kezelése - TELJESEN BIZTONSÁGOS VERZIÓ
        try {
            console.log("🔄 Foglalás frissítve (App):", detail);
            
            // Biztonságos adat kinyerés
            const hasReservation = this.safeGetReservationStatus(detail);
            const reservationCount = this.safeGetReservationCount(detail);
            
            console.log(`🔄 Foglalás frissítve: ${hasReservation ? 'Van foglalás' : 'Nincs foglalás'} (${reservationCount} db)`);
            
            // Frissítsük a UI-t
            this.updateReservationUI(hasReservation, reservationCount);
            
            // Mentés localStorage-ba
            localStorage.setItem('hasReservation', hasReservation.toString());
            localStorage.setItem('reservationCount', reservationCount.toString());
            
            // Debug információk
            if (detail && detail.reservation) {
                console.log("📋 Foglalás adatok:", {
                    id: detail.reservation.id || detail.reservation.reservationId || 'nincs',
                    status: detail.reservation.status || 'nincs',
                    table: detail.reservation.tableName || detail.reservation.tableNumber || 'nincs',
                    date: detail.reservation.date || 'nincs',
                    time: detail.reservation.time || 'nincs'
                });
            }
            
        } catch (error) {
            console.error("❌ Hiba a foglalás frissítés kezelésekor:", error);
            console.error("🔍 Detail objektum:", detail);
            
            // Alapértelmezett értékekkel folytatjuk
            this.updateReservationUI(false, 0);
        }
    }
    
    // SEGÉDMETÓDUSOK A FOGLALÁS ADATOK BIZTONSÁGOS KINYERÉSÉHEZ
    safeGetReservationStatus(detail) {
        try {
            if (!detail) return false;
            
            // Különböző módon ellenőrizzük, hogy van-e foglalás
            if (detail.hasReservation !== undefined) {
                return Boolean(detail.hasReservation);
            }
            
            if (detail.userReservations !== undefined) {
                return Number(detail.userReservations) > 0;
            }
            
            if (detail.reservationCount !== undefined) {
                return Number(detail.reservationCount) > 0;
            }
            
            if (detail.reservation) {
                return Object.keys(detail.reservation).length > 0;
            }
            
            if (detail.reservations && Array.isArray(detail.reservations)) {
                return detail.reservations.length > 0;
            }
            
            return false;
        } catch (e) {
            console.warn("⚠️ Hiba a foglalás státusz lekérdezésekor:", e);
            return false;
        }
    }
    
    safeGetReservationCount(detail) {
        try {
            if (!detail) return 0;
            
            // Különböző forrásokból próbáljuk kinyerni a számot
            if (detail.userReservations !== undefined) {
                return Number(detail.userReservations) || 0;
            }
            
            if (detail.reservationCount !== undefined) {
                return Number(detail.reservationCount) || 0;
            }
            
            if (detail.reservations && Array.isArray(detail.reservations)) {
                return detail.reservations.length;
            }
            
            if (detail.reservation && Object.keys(detail.reservation).length > 0) {
                return 1;
            }
            
            return 0;
        } catch (e) {
            console.warn("⚠️ Hiba a foglalás szám lekérdezésekor:", e);
            return 0;
        }
    }
    
    onAuthStateChanged(detail) {
        console.log('👤 Auth állapot változás kezelése:', detail);
        
        try {
            if (detail?.isAuthenticated && detail?.userData) {
                console.log(`✅ Felhasználó bejelentkezve: ${detail.userData.email || detail.userData.username || detail.userData.id || 'Ismeretlen felhasználó'}`);
                this.onUserLoggedIn(detail.userData);
            } else {
                console.log('👤 Felhasználó kijelentkezve vagy nincs adat');
                this.onUserLoggedOut();
            }
        } catch (error) {
            console.error("❌ Hiba az auth állapot kezelésekor:", error);
            // Alapértelmezettként kijelentkezett állapot
            this.onUserLoggedOut();
        }
    }
    
    onUserLoggedIn(userData = null) {
        console.log('🎉 Felhasználó bejelentkezett');
        
        try {
            // Frissítsük a felhasználói adatokat a localStorage-ban
            localStorage.setItem('userLoggedIn', 'true');
            
            if (userData) {
                if (userData.email) {
                    localStorage.setItem('userEmail', userData.email);
                }
                if (userData.username) {
                    localStorage.setItem('userName', userData.username);
                }
                if (userData.id) {
                    localStorage.setItem('userId', userData.id);
                }
            }
            
            // Kosár szinkronizálás, ha van CartManager
            if (window.cartManager && userData?.id && typeof window.cartManager.syncUserCart === 'function') {
                setTimeout(() => {
                    try {
                        window.cartManager.syncUserCart(userData.id);
                    } catch (e) {
                        console.warn('⚠️ Kosár szinkronizálás sikertelen:', e);
                    }
                }, 500);
            }
            
            // UI frissítés
            this.updateAuthUI(true, userData);
            
        } catch (error) {
            console.error("❌ Hiba a felhasználó bejelentkezés kezelésekor:", error);
            this.updateAuthUI(false, null);
        }
    }
    
    onUserLoggedOut() {
        console.log('👋 Felhasználó kijelentkezett');
        
        try {
            localStorage.removeItem('userLoggedIn');
            localStorage.removeItem('userEmail');
            localStorage.removeItem('userName');
            localStorage.removeItem('userId');
            
            // UI frissítés
            this.updateAuthUI(false, null);
            
        } catch (error) {
            console.error("❌ Hiba a felhasználó kijelentkezés kezelésekor:", error);
        }
    }
    
    onOnlineStatusChanged(isOnline) {
        console.log(isOnline ? '🌐 Online állapot' : '📴 Offline állapot');
        
        try {
            // Online státusz megjelenítése
            this.showOnlineStatus(isOnline);
            
            if (isOnline) {
                // Szinkronizálás online állapotban
                this.syncData();
            }
        } catch (error) {
            console.error("❌ Hiba az online állapot kezelésekor:", error);
        }
    }
    
    onVisibilityChange(isHidden) {
        if (!isHidden && document.visibilityState === 'visible') {
            // Oldal újra láthatóvá válik
            console.log('👀 Oldal újra látható, frissítés...');
            try {
                this.refreshUI();
            } catch (error) {
                console.error("❌ Hiba az oldal láthatóság változás kezelésekor:", error);
            }
        }
    }
    
    // UI frissítési metódusok
    updateCartCountUI(count) {
        try {
            // Keressük meg az összes kosár számláló elemet
            const cartCountElements = document.querySelectorAll('.cart-count, .cart-item-count, [data-cart-count], .cart-badge');
            
            const totalItems = Number(count) || 0;
            const displayCount = totalItems > 99 ? '99+' : totalItems.toString();
            
            cartCountElements.forEach(element => {
                if (element.classList.contains('cart-badge')) {
                    element.textContent = displayCount;
                    element.style.display = totalItems > 0 ? 'flex' : 'none';
                } else {
                    element.textContent = totalItems > 0 ? displayCount : '';
                    element.style.display = totalItems > 0 ? 'inline-block' : 'none';
                }
                
                // Animáció hozzáadása
                if (totalItems > 0) {
                    element.classList.add('cart-updated');
                    setTimeout(() => {
                        element.classList.remove('cart-updated');
                    }, 500);
                }
            });
            
            // Ha a kosár üres, frissítsük a megjelenést
            if (count === 0) {
                const emptyCartElements = document.querySelectorAll('.cart-empty, .empty-cart-message');
                emptyCartElements.forEach(element => {
                    element.style.display = 'block';
                });
            }
        } catch (error) {
            console.warn("⚠️ Hiba a kosár számláló frissítésekor:", error);
        }
    }
    
    updateAuthUI(isLoggedIn, userData = null) {
        try {
            // Keressük meg az auth UI elemeket
            const loginElements = document.querySelectorAll('.login-btn, .auth-login, [data-auth="login"]');
            const logoutElements = document.querySelectorAll('.logout-btn, .auth-logout, [data-auth="logout"]');
            const profileElements = document.querySelectorAll('.profile-btn, .user-profile, [data-auth="profile"]');
            const userInfoElements = document.querySelectorAll('.user-info, .user-name, [data-user-name]');
            
            if (isLoggedIn) {
                // Bejelentkezett állapot
                loginElements.forEach(el => {
                    if (el) el.style.display = 'none';
                });
                logoutElements.forEach(el => {
                    if (el) el.style.display = 'block';
                });
                profileElements.forEach(el => {
                    if (el) el.style.display = 'block';
                });
                
                // Felhasználói információk
                const userName = userData?.username || userData?.email || 'Felhasználó';
                userInfoElements.forEach(el => {
                    if (el) {
                        el.textContent = userName;
                        el.style.display = 'block';
                    }
                });
            } else {
                // Kijelentkezett állapot
                loginElements.forEach(el => {
                    if (el) el.style.display = 'block';
                });
                logoutElements.forEach(el => {
                    if (el) el.style.display = 'none';
                });
                profileElements.forEach(el => {
                    if (el) el.style.display = 'none';
                });
                userInfoElements.forEach(el => {
                    if (el) el.style.display = 'none';
                });
            }
        } catch (error) {
            console.warn("⚠️ Hiba az auth UI frissítésekor:", error);
        }
    }
    
    updateReservationUI(hasReservation, count = 0) {
        try {
            const reservationElements = document.querySelectorAll('.reservation-badge, [data-reservation-count]');
            const reservationNotice = document.querySelectorAll('.reservation-notice, [data-reservation-notice]');
            
            const reservationCount = Number(count) || 0;
            
            reservationElements.forEach(element => {
                if (element) {
                    if (reservationCount > 0) {
                        element.textContent = reservationCount.toString();
                        element.style.display = 'inline-block';
                    } else {
                        element.style.display = 'none';
                    }
                }
            });
            
            reservationNotice.forEach(element => {
                if (element) {
                    if (hasReservation) {
                        element.textContent = `Van ${reservationCount} foglalásod`;
                        element.style.display = 'block';
                    } else {
                        element.style.display = 'none';
                    }
                }
            });
        } catch (error) {
            console.warn("⚠️ Hiba a foglalás UI frissítésekor:", error);
        }
    }
    
    updateCartUI(cartData) {
        try {
            // Alapértelmezett implementáció - bővíthető
            const totalItems = cartData?.totalItems || 0;
            this.updateCartCountUI(totalItems);
        } catch (error) {
            console.warn("⚠️ Hiba a kosár UI frissítésekor:", error);
        }
    }
    
    showOnlineStatus(isOnline) {
        try {
            // Online státusz megjelenítése
            const statusElement = document.getElementById('online-status') || 
                                 document.querySelector('.online-status');
            
            if (statusElement) {
                statusElement.textContent = isOnline ? 'Online' : 'Offline';
                statusElement.className = isOnline ? 'online-status online' : 'online-status offline';
                statusElement.style.display = 'block';
                
                // Eltűntetés időzítése
                setTimeout(() => {
                    if (statusElement) {
                        statusElement.style.display = 'none';
                    }
                }, 3000);
            }
        } catch (error) {
            console.warn("⚠️ Hiba az online státusz megjelenítésekor:", error);
        }
    }
    
    refreshUI() {
        console.log('🔄 UI frissítése...');
        
        try {
            // Frissítsük a kosár számlálót
            if (window.cartManager && window.cartManager.getCartData) {
                try {
                    const cartData = window.cartManager.getCartData();
                    this.updateCartUI(cartData);
                } catch (e) {
                    console.warn('⚠️ Kosár adatok frissítése sikertelen:', e);
                }
            }
            
            // Frissítsük a felhasználói állapotot
            if (window.authManager && window.authManager.getAuthState) {
                try {
                    const authState = window.authManager.getAuthState();
                    this.updateAuthUI(authState.isAuthenticated, authState.userData);
                } catch (e) {
                    console.warn('⚠️ Auth állapot frissítése sikertelen:', e);
                }
            }
            
            // Frissítsük a foglalásokat
            if (window.cartManager && window.cartManager.getReservationData) {
                try {
                    const reservationData = window.cartManager.getReservationData();
                    const hasReservation = this.safeGetReservationStatus(reservationData);
                    const reservationCount = this.safeGetReservationCount(reservationData);
                    this.updateReservationUI(hasReservation, reservationCount);
                } catch (e) {
                    console.warn('⚠️ Foglalás adatok frissítése sikertelen:', e);
                }
            }
            
            console.log('✅ UI frissítve');
            
        } catch (error) {
            console.error("❌ Hiba az UI frissítésekor:", error);
        }
    }
    
    syncData() {
        console.log('🔄 Adatok szinkronizálása...');
        
        try {
            // Kosár szinkronizálás
            if (window.cartManager && typeof window.cartManager.sync === 'function') {
                setTimeout(() => {
                    try {
                        window.cartManager.sync();
                    } catch (e) {
                        console.warn('⚠️ Kosár szinkronizálás sikertelen:', e);
                    }
                }, 1000);
            }
        } catch (error) {
            console.error("❌ Hiba az adatok szinkronizálásakor:", error);
        }
    }
    
    // Oldal specifikus metódusok - ALAPÉRTELMEZETT (üres) IMPLEMENTÁCIÓK
    async initHomePage() {
        console.log('🏠 Főoldal inicializálása');
        // Főoldal specifikus kód
    }
    
    async initCartPage() {
        console.log('🛒 Kosár oldal inicializálása');
        // Kosár oldal specifikus kód
    }
    
    async initMenuPage() {
        console.log('🍽️ Menü oldal inicializálása');
        // Menü oldal specifikus kód
    }
    
    async initReservationPage() {
        console.log('📅 Foglalás oldal inicializálása');
        // Foglalás oldal specifikus kód
    }
    
    async initCheckoutPage() {
        console.log('💳 Pénztár oldal inicializálása');
        // Pénztár oldal specifikus kód
    }
    
    async initProfilePage() {
        console.log('👤 Profil oldal inicializálása');
        // Profil oldal specifikus kód
    }
    
    async initLoginPage() {
        console.log('🔑 Bejelentkezés oldal inicializálása');
        // Bejelentkezés oldal specifikus kód
    }
    
    async initRegisterPage() {
        console.log('📝 Regisztráció oldal inicializálása');
        // Regisztráció oldal specifikus kód
    }
    
    // Segédfüggvények
    getDebugInfo() {
        return {
            appInitialized: this.isInitialized,
            initializationPhase: this.initializationPhase,
            dependencies: this.dependencies,
            authManager: !!window.authManager,
            authManagerInitialized: window.authManager?.isInitialized || false,
            cartManager: !!window.cartManager,
            cartManagerInitialized: window.cartManager?.isInitialized || false,
            navbar: !!window.navbar,
            navbarLoaded: window.navbar?.navbarLoaded || false,
            page: window.location.pathname,
            timestamp: new Date().toISOString(),
            online: navigator.onLine
        };
    }
    
    showDebugInfo() {
        const info = this.getDebugInfo();
        console.group('🔍 App Debug Információ');
        Object.entries(info).forEach(([key, value]) => {
            console.log(`${key}:`, value);
        });
        console.groupEnd();
        
        return info;
    }
    
    cleanup() {
        console.log('🧹 App takarítás...');
        
        try {
            // Távolítsuk el az eseménykezelőket
            window.removeEventListener('cartManagerReady', this.onCartManagerReady);
            window.removeEventListener('cartUpdated', this.onCartUpdated);
            window.removeEventListener('reservationUpdated', this.onReservationUpdated);
            window.removeEventListener('authStateChanged', this.onAuthStateChanged);
            
            this.isInitialized = false;
            this.initializationPhase = 'cleaned';
            console.log('✅ App takarítva');
            
        } catch (error) {
            console.error("❌ Hiba az app takarításakor:", error);
        }
    }
}

// Globális App instance
let app;

// Dokumentum betöltésekor indítjuk az alkalmazást - EGYSZERŰSÍTETT
$(document).ready(function() {
    console.log('📄 Dokumentum betöltve, alkalmazás indítása...');
    
    // Várjunk egy kicsit, hogy a többi komponens is betöltődjön
    setTimeout(() => {
        startApp();
    }, 100);
});

function startApp() {
    // Ha már fut az app, ne indítsuk újra
    if (window.app && window.app.isInitialized) {
        console.log('ℹ️ App már fut, frissítés...');
        try {
            window.app.refreshUI();
        } catch (error) {
            console.error("❌ Hiba az app frissítésekor:", error);
        }
        return;
    }
    
    console.log('🚀 App indítása...');
    app = new App();
    
    // Indítsuk el az appot késleltetéssel
    setTimeout(() => {
        try {
            app.init();
        } catch (error) {
            console.error("❌ Hiba az app inicializálásakor:", error);
        }
    }, 200);
}

// Globális elérhetőség
window.App = App;
window.app = app;

// Esemény a dokumentum elhagyásakor
$(window).on('beforeunload', function() {
    if (app && typeof app.cleanup === 'function') {
        try {
            app.cleanup();
        } catch (error) {
            console.error("❌ Hiba az app takarításakor beforeunload eseményben:", error);
        }
    }
});

// App újraindítása globális függvényként
window.restartApp = function() {
    console.log('🔄 App újraindítása...');
    try {
        if (app && typeof app.cleanup === 'function') {
            app.cleanup();
        }
        app = new App();
        setTimeout(() => app.init(), 300);
    } catch (error) {
        console.error("❌ Hiba az app újraindításakor:", error);
        // Próbáljuk meg alaphelyzetbe állítani
        app = new App();
        setTimeout(() => {
            try {
                app.init();
            } catch (e) {
                console.error("❌ Hiba az app újraindítás utáni inicializálásakor:", e);
            }
        }, 500);
    }
};
if (App.prototype.refreshUI) {
    const originalRefreshUI = App.prototype.refreshUI;
    App.prototype.refreshUI = function() {
        originalRefreshUI.call(this);
        
        // Jogosultság alapú UI védelem
        if (window.authManager && window.authManager.protectPageElements) {
            setTimeout(() => {
                window.authManager.protectPageElements();
            }, 50);
        }
    };
}

// Új metódus a felhasználói szerepkör lekéréséhez
App.prototype.getUserRoles = function() {
    return window.authManager?.permissionManager?.userRoles || ['guest'];
};

// Új metódus a jogosultság ellenőrzéshez
App.prototype.can = function(permission) {
    return window.authManager?.hasPermission(permission) || false;
};

// Új metódus a szerepkör ellenőrzéshez
App.prototype.is = function(role) {
    return window.authManager?.hasRole(role) || false;
};

console.log('✅ App.js jogosultságkezeléssel kiegészítve');

console.log('🚀 App osztály betöltve');