
// combined-modal.js - JAVÍTOTT VERZIÓ (kijelentkezés megszakítás fix)
class CombinedModal {
    constructor() {
        this.activeModals = new Set();
        this.logoutRedirectUrl = 'index.html';
        this.loginRedirectUrl = 'index.html';
        this.currentEscHandler = null;
        this.modalStates = new Map(); // <-- ÚJ: Modal állapotok tárolása
        this.originalSessionId = null; // <-- ÚJ: Az eredeti session ID tárolása
        this.init();
        console.log('🎨 CombinedModal inicializálva');
    }

    init() {
        this.injectStyles();
        this.createModalContainer();
        this.overrideAlert();
        this.setupLogoutInterception();
        this.interceptLoginForms();
        console.log('✅ CombinedModal készen áll');
    }

    // CSS STÍLUSOK - változatlan
    injectStyles() {
        if (document.getElementById('combined-modal-styles')) return;

        const styles = `
            /* KÖZÖS STÍLUSOK */
            .combined-modal-overlay {
                position: fixed;
                top: 0;
                left: 0;
                width: 100vw;
                height: 100vh;
                background: rgba(0, 0, 0, 0.85);
                backdrop-filter: blur(10px);
                display: none;
                justify-content: center;
                align-items: center;
                z-index: 99999;
                opacity: 0;
                transition: opacity 0.4s ease;
                padding: 20px;
                box-sizing: border-box;
            }

            .combined-modal-overlay.active {
                display: flex;
                opacity: 1;
            }

            .combined-modal {
                background: linear-gradient(135deg, var(--gray-900, #1a1a1a) 0%, var(--gray-800, #262626) 100%);
                border-radius: 20px;
                border: 2px solid var(--accent, #ff6b35);
                box-shadow: 0 30px 100px rgba(0, 0, 0, 0.8);
                width: 100%;
                max-width: 500px;
                transform: scale(0.9) translateY(20px);
                opacity: 0;
                transition: all 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275);
                overflow: hidden;
                position: relative;
            }

            [data-theme="light"] .combined-modal {
                background: linear-gradient(135deg, #ffffff 0%, #f8fafc 100%);
                border: 2px solid var(--accent, #89E6F6);
            }

            .combined-modal.active {
                transform: scale(1) translateY(0);
                opacity: 1;
            }

            /* FEJLÉC SZÍNEK */
            .combined-modal-header.logout {
                background: linear-gradient(135deg, var(--accent, #ff6b35) 0%, #e55a2b 100%);
            }
            
            .combined-modal-header.cancelled {
                background: linear-gradient(135deg, #6c757d 0%, #495057 100%);
            }
            
            .combined-modal-header.success {
                background: linear-gradient(135deg, #28a745 0%, #20c997 100%);
            }
            
            .combined-modal-header.login {
                background: linear-gradient(135deg, #28a745 0%, #20c997 100%);
            }
            
            .combined-modal-header.error {
                background: linear-gradient(135deg, #dc3545 0%, #c82333 100%);
            }

            .combined-modal-header {
                color: white;
                padding: 1.8rem 2rem;
                text-align: center;
                position: relative;
                transition: all 0.3s ease;
            }

            .combined-modal-title {
                font-family: 'Merriweather', serif;
                font-weight: 700;
                font-size: 1.6rem;
                margin: 0;
                display: flex;
                align-items: center;
                justify-content: center;
                gap: 1rem;
            }

            .combined-modal-body {
                padding: 2.5rem;
                color: var(--white, #ffffff);
                text-align: center;
                font-size: 1.15rem;
                line-height: 1.7;
                background: var(--gray-800, #262626);
                transition: all 0.3s ease;
            }

            [data-theme="light"] .combined-modal-body {
                color: var(--gray-900, #1a1a1a);
                background: #ffffff;
            }

            /* IKONOK */
            .combined-icon {
                font-size: 5rem;
                margin-bottom: 1.5rem;
                display: inline-block;
            }
            
            .combined-icon.logout {
                color: var(--accent, #ff6b35);
                animation: floatIcon 3s ease-in-out infinite;
            }
            
            .combined-icon.cancelled {
                color: #6c757d;
                animation: none;
            }
            
            .combined-icon.success {
                color: #28a745;
            }
            
            .combined-icon.login {
                color: #28a745;
                animation: floatIcon 3s ease-in-out infinite;
            }
            
            .combined-icon.error {
                color: #dc3545;
            }

            /* VISSZASZÁMLÁLÁS */
            .combined-countdown {
                font-size: 4.5rem;
                font-weight: 800;
                font-family: 'Merriweather', serif;
                text-shadow: 0 5px 20px rgba(255, 107, 53, 0.5);
                transition: all 0.3s ease;
                margin: 1.5rem 0;
            }
            
            .combined-countdown.logout {
                color: var(--accent, #ff6b35);
                animation: pulseCountdown 1.5s infinite;
            }
            
            .combined-countdown.cancelled {
                color: #6c757d;
                animation: none;
            }
            
            .combined-countdown.login {
                color: #28a745;
                animation: pulseCountdown 1.5s infinite;
            }
            
            .combined-countdown.error {
                color: #dc3545;
                animation: pulseError 1s infinite;
            }

            /* SZÖVEGEK */
            .combined-text {
                color: var(--gray-400, #a3a3a3);
                font-size: 1rem;
                margin-top: 0.5rem;
                transition: all 0.3s ease;
            }
            
            .combined-text.cancelled {
                color: #28a745;
                font-weight: 600;
            }
            
            .combined-text.login {
                color: var(--gray-400, #a3a3a3);
            }
            
            .combined-text.success {
                color: #28a745;
                font-weight: 600;
            }
            
            .combined-text.error {
                color: #dc3545;
                font-weight: 600;
            }

            /* FOOTER */
            .combined-modal-footer {
                padding: 1.8rem 2rem;
                display: flex;
                gap: 1.2rem;
                justify-content: center;
                background: var(--gray-900, #1a1a1a);
                border-top: 1px solid var(--gray-700, #404040);
                transition: all 0.3s ease;
            }

            [data-theme="light"] .combined-modal-footer {
                background: #f8fafc;
                border-top: 1px solid var(--gray-300, #d4d4d4);
            }

            /* GOMBOK */
            .combined-btn {
                padding: 1rem 2rem;
                border-radius: 12px;
                border: none;
                font-weight: 600;
                font-size: 1.05rem;
                cursor: pointer;
                transition: all 0.3s ease;
                display: flex;
                align-items: center;
                justify-content: center;
                gap: 0.8rem;
                min-width: 140px;
                flex: 1;
            }

            .combined-btn:hover {
                transform: translateY(-3px);
                box-shadow: 0 10px 25px rgba(0, 0, 0, 0.3);
            }

            /* GOMB SZÍNEK */
            .btn-cancel {
                background: var(--gray-700, #404040);
                color: var(--white, #ffffff);
                border: 1px solid var(--gray-600, #525252);
            }
            
            .btn-cancel.cancelled {
                background: #6c757d;
                color: white;
            }

            .btn-primary {
                background: linear-gradient(135deg, var(--accent, #ff6b35) 0%, #e55a2b 100%);
                color: white;
                border: 1px solid var(--accent, #ff6b35);
            }
            
            .btn-primary.success {
                background: linear-gradient(135deg, #28a745 0%, #20c997 100%);
                border: 1px solid #28a745;
            }
            
            .btn-primary.login {
                background: linear-gradient(135deg, #28a745 0%, #20c997 100%);
                color: white;
                border: 1px solid #28a745;
            }
            
            .btn-primary.back {
                background: linear-gradient(135deg, #17a2b8 0%, #138496 100%);
                border: 1px solid #17a2b8;
            }

            .btn-secondary {
                background: linear-gradient(135deg, #6c757d 0%, #495057 100%);
                color: white;
                border: 1px solid #6c757d;
            }
            
            .btn-warning {
                background: linear-gradient(135deg, #ffc107 0%, #ffb347 100%);
                color: #000000;
                border: 1px solid #ffc107;
            }

            [data-theme="light"] .btn-primary {
                background: linear-gradient(135deg, #89E6F6 0%, #8EC5FF 100%);
                color: #000000;
                border: 1px solid #89E6F6;
            }
            
            [data-theme="light"] .btn-primary.success {
                background: linear-gradient(135deg, #28a745 0%, #20c997 100%);
                color: white;
            }

            /* ANIMÁCIÓK */
            @keyframes floatIcon {
                0%, 100% { transform: translateY(0); }
                50% { transform: translateY(-15px); }
            }

            @keyframes pulseCountdown {
                0%, 100% { transform: scale(1); opacity: 1; }
                50% { transform: scale(1.1); opacity: 0.8; }
            }

            @keyframes pulseError {
                0%, 100% { transform: scale(1); opacity: 1; }
                50% { transform: scale(1.05); opacity: 0.8; }
            }
        `;

        const styleSheet = document.createElement('style');
        styleSheet.id = 'combined-modal-styles';
        styleSheet.textContent = styles;
        document.head.appendChild(styleSheet);
    }

    createModalContainer() {
        if (document.getElementById('combined-modal-container')) return;

        const container = document.createElement('div');
        container.id = 'combined-modal-container';
        document.body.appendChild(container);
    }

    /* ========== LOGOUT FUNKCIÓK ========== */
    overrideAlert() {
        const originalAlert = window.alert;
        
        window.alert = (message) => {
            console.log('🔔 Alert átirányítva:', message);
            
            if (typeof message === 'string' && 
                (message.toLowerCase().includes('kijelentkez') || 
                 message.includes('Kijelentkezés'))) {
                
                this.showLogoutModal();
                return true;
            }
            
            this.showSimpleModal(message, 'Információ', 3);
            return true;
        };
    }

    setupLogoutInterception() {
        document.addEventListener('click', (e) => {
            const target = e.target;
            
            if (target.id === 'logoutBtn' || 
                target.id === 'logout-button' ||
                (target.closest && target.closest('#logoutBtn')) ||
                (target.closest && target.closest('#logout-button'))) {
                
                e.preventDefault();
                e.stopPropagation();
                
                console.log('🔔 Logout gomb kattintva');
                
                // Elmentjük az eredeti session ID-t
                this.originalSessionId = this.getCookie('SessionID');
                console.log('💾 Eredeti session ID elmentve:', this.originalSessionId);
                
                this.showLogoutModal();
                
                return false;
            }
        }, true);
    }

    callLogoutAPI() {
        // Először elmentjük a session ID-t
        const currentSessionId = this.getCookie('SessionID');
        console.log('📞 Logout API hívás, session:', currentSessionId);
        
        document.cookie = "SessionID=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;";
        
        if (typeof $ !== 'undefined') {
            $.ajax({
                type: "POST",
                url: "/Session/Logout",
                headers: { 
                    'RequestVerificationToken': this.getCookie('CSRF-TOKEN') 
                },
                timeout: 3000
            }).then(() => {
                console.log('✅ Logout API sikeres');
            }).catch((err) => {
                console.warn('⚠️ Logout API hiba:', err);
            });
        }
    }

    showLogoutModal() {
        const modalId = 'logout-modal-' + Date.now();
        this.activeModals.add(modalId);
        
        // Modal állapot inicializálása
        this.modalStates.set(modalId, {
            countdown: 5,
            isCancelled: false,
            countdownInterval: null,
            logoutApiCalled: false // ÚJ: Tároljuk, hogy meghívtuk-e már a logout API-t
        });

        const state = this.modalStates.get(modalId);

        const modalHtml = `
            <div class="combined-modal-overlay" id="${modalId}">
                <div class="combined-modal">
                    <div class="combined-modal-header logout">
                        <h3 class="combined-modal-title">
                            <i class="bi bi-box-arrow-right"></i>
                            Kijelentkezés
                        </h3>
                    </div>
                    <div class="combined-modal-body">
                        <div class="combined-icon logout">
                            <i class="bi bi-hourglass-split"></i>
                        </div>
                        
                        <h4 style="margin-bottom: 1.5rem; font-size: 1.4rem;" id="main-text-${modalId}">
                            Kijelentkezés folyamatban...
                        </h4>
                        
                        <div class="combined-countdown logout" id="countdown-${modalId}">
                            ${state.countdown}
                        </div>
                        
                        <div class="combined-text" id="text-${modalId}">
                            Automatikus kijelentkezés <strong>${state.countdown}</strong> másodperc múlva...
                        </div>
                    </div>
                    
                    <div class="combined-modal-footer">
                        <button class="combined-btn btn-cancel" id="cancel-${modalId}">
                            <i class="bi bi-x-circle"></i>
                            Mégse
                        </button>
                        
                        <button class="combined-btn btn-primary" id="home-${modalId}">
                            <i class="bi bi-house-fill"></i>
                            Főoldal
                        </button>
                    </div>
                </div>
            </div>
        `;

        this.insertModal(modalHtml);

        setTimeout(() => {
            this.activateModal(modalId);
        }, 50);

        // GOMB ESEMÉNYKEZELŐK
        const cancelBtn = document.getElementById(`cancel-${modalId}`);
        const homeBtn = document.getElementById(`home-${modalId}`);
        
        // MÉGSE gomb - JAVÍTOTT
        if (cancelBtn) {
            cancelBtn.onclick = () => {
                console.log('❌ Kijelentkezés megszakítva');
                state.isCancelled = true;
                if (state.countdownInterval) clearInterval(state.countdownInterval);
                
                this.updateLogoutToCancelled(modalId);
                
                // Gombok frissítése
                cancelBtn.innerHTML = '<i class="bi bi-check-circle"></i> Megszakítva';
                cancelBtn.disabled = true;
                cancelBtn.classList.add('cancelled');
                cancelBtn.style.opacity = '0.7';
                
                homeBtn.innerHTML = '<i class="bi bi-arrow-return-left"></i> Vissza a weboldalra';
                homeBtn.classList.remove('btn-primary');
                homeBtn.classList.add('btn-primary', 'back');
                
                // HOME gomb új működése
                homeBtn.onclick = () => {
                    this.closeModal(modalId);
                };
                
                // Session visszaállítása - JAVÍTOTT
                this.restoreSession();
            };
        }
        
        // FŐOLDAL gomb - JAVÍTOTT
        if (homeBtn) {
            homeBtn.onclick = () => {
                if (state.countdownInterval) clearInterval(state.countdownInterval);
                
                // Csak akkor hívjuk meg a logout API-t, ha még nem hívtuk
                if (!state.logoutApiCalled) {
                    state.logoutApiCalled = true;
                    this.callLogoutAPI();
                }
                
                this.updateLogoutToSuccess(modalId);
                
                // Gombok elrejtése
                cancelBtn.style.display = 'none';
                homeBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Átirányítás...';
                homeBtn.disabled = true;
                
                // 1 másodperc múlva átirányítás
                setTimeout(() => {
                    this.redirectToHome(modalId);
                }, 1000);
            };
        }

        // VISSZASZÁMLÁLÁS - JAVÍTOTT
        state.countdownInterval = this.startCountdown(modalId, state.countdown, () => {
            if (homeBtn && !state.isCancelled) {
                // Csak akkor hívjuk meg a logout API-t, ha még nem hívtuk
                if (!state.logoutApiCalled) {
                    state.logoutApiCalled = true;
                    this.callLogoutAPI();
                }
                homeBtn.click();
            }
        }, 'logout');

        // ESC billentyű
        this.setupEscHandler(modalId, () => {
            if (!state.isCancelled) {
                if (cancelBtn) cancelBtn.click();
            } else {
                this.closeModal(modalId);
            }
        });
    }

    updateLogoutToCancelled(modalId) {
        // FEJLÉC MÓDOSÍTÁSA
        const header = document.querySelector(`#${modalId} .combined-modal-header`);
        if (header) {
            header.classList.remove('logout');
            header.classList.add('cancelled');
            header.querySelector('.combined-modal-title').innerHTML = 
                '<i class="bi bi-slash-circle"></i> Kijelentkezés megszakítva';
        }
        
        // FŐ SZÖVEG MÓDOSÍTÁSA
        const mainText = document.getElementById(`main-text-${modalId}`);
        if (mainText) {
            mainText.textContent = 'Kijelentkezés megszakítva!';
            mainText.style.color = '#28a745';
        }
        
        // IKON MÓDOSÍTÁSA
        const icon = document.querySelector(`#${modalId} .combined-icon`);
        if (icon) {
            icon.classList.remove('logout');
            icon.classList.add('cancelled');
            icon.innerHTML = '<i class="bi bi-slash-circle"></i>';
            icon.style.animation = 'none';
        }
        
        // VISSZASZÁMLÁLÁS MÓDOSÍTÁSA
        const countdownEl = document.getElementById(`countdown-${modalId}`);
        const textEl = document.getElementById(`text-${modalId}`);
        
        if (countdownEl) {
            countdownEl.textContent = '✋';
            countdownEl.classList.add('cancelled');
            countdownEl.style.color = '#6c757d';
        }
        
        if (textEl) {
            textEl.textContent = 'Maradsz bejelentkezve.';
            textEl.classList.add('cancelled');
        }
    }

    updateLogoutToSuccess(modalId) {
        // FEJLÉC MÓDOSÍTÁSA
        const header = document.querySelector(`#${modalId} .combined-modal-header`);
        if (header) {
            header.classList.remove('logout');
            header.classList.add('success');
            header.querySelector('.combined-modal-title').innerHTML = 
                '<i class="bi bi-check-circle"></i> Sikeres kijelentkezés';
        }
        
        const mainText = document.getElementById(`main-text-${modalId}`);
        if (mainText) {
            mainText.textContent = 'Sikeresen kijelentkeztél!';
            mainText.style.color = '#28a745';
        }
        
        const icon = document.querySelector(`#${modalId} .combined-icon`);
        if (icon) {
            icon.classList.remove('logout');
            icon.classList.add('success');
            icon.innerHTML = '<i class="bi bi-check-circle"></i>';
        }
        
        const countdownEl = document.getElementById(`countdown-${modalId}`);
        const textEl = document.getElementById(`text-${modalId}`);
        
        if (countdownEl) {
            countdownEl.style.display = 'none';
        }
        
        if (textEl) {
            textEl.textContent = 'Átirányítás a főoldalra...';
            textEl.style.color = '#28a745';
        }
    }

    /* ========== LOGIN FUNKCIÓK ========== */
    interceptLoginForms() {
        document.addEventListener('submit', (e) => {
            const form = e.target;
            
            // Ha login form
            if (form.id === 'loginForm' || 
                form.querySelector('input[name*="UserName"]') || 
                form.querySelector('input[name*="Password"]')) {
                
                e.preventDefault();
                e.stopPropagation();
                
                console.log('🔔 Bejelentkezési form elküldve');
                
                // Alapvető validáció
                const username = form.querySelector('input[name*="UserName"], #UserName')?.value;
                const password = form.querySelector('input[name*="Password"], #UserPassword')?.value;
                
                if (!username || !password) {
                    this.showErrorModal('Kérlek, tölts ki minden mezőt!');
                    return;
                }
                
                // Megjelenítjük a login modalt
                this.showLoginModal(username, form);
            }
        }, true);
    }

    showLoginModal(username, form) {
        const modalId = 'login-modal-' + Date.now();
        this.activeModals.add(modalId);
        
        // Modal állapot inicializálása
        this.modalStates.set(modalId, {
            countdown: 3,
            isSuccess: false,
            isError: false,
            countdownInterval: null,
            redirectTimeout: null,
            redirectInterval: null,
            redirectCountdown: 3
        });

        const state = this.modalStates.get(modalId);

        const modalHtml = `
            <div class="combined-modal-overlay" id="${modalId}">
                <div class="combined-modal">
                    <div class="combined-modal-header login">
                        <h3 class="combined-modal-title">
                            <i class="bi bi-person-check"></i>
                            Bejelentkezés
                        </h3>
                    </div>
                    <div class="combined-modal-body">
                        <div class="combined-icon login">
                            <i class="bi bi-hourglass-split"></i>
                        </div>
                        
                        <h4 style="margin-bottom: 1.5rem; font-size: 1.4rem;" id="main-text-${modalId}">
                            Bejelentkezés folyamatban...
                        </h4>
                        
                        <p id="user-info-${modalId}" style="margin-bottom: 1rem;">
                            Felhasználó: <strong>${username}</strong>
                        </p>
                        
                        <div class="combined-countdown login" id="countdown-${modalId}">
                            ${state.countdown}
                        </div>
                        
                        <div class="combined-text" id="text-${modalId}">
                            Sikeres bejelentkezés <strong>${state.countdown}</strong> másodperc múlva...
                        </div>
                    </div>
                    
                    <div class="combined-modal-footer">
                        <button class="combined-btn btn-cancel" id="close-${modalId}">
                            <i class="bi bi-x-circle"></i>
                            Mégse
                        </button>
                        
                        <button class="combined-btn btn-primary login" id="dashboard-${modalId}" disabled>
                            <i class="bi bi-person-circle"></i>
                            Profil
                        </button>
                    </div>
                </div>
            </div>
        `;

        this.insertModal(modalHtml);

        setTimeout(() => {
            this.activateModal(modalId);
        }, 50);

        // GOMB ESEMÉNYKEZELŐK
        const closeBtn = document.getElementById(`close-${modalId}`);
        const dashboardBtn = document.getElementById(`dashboard-${modalId}`);
        
        // BEZÁRÁS gomb
        if (closeBtn) {
            closeBtn.onclick = () => {
                console.log('❌ Bejelentkezés megszakítva');
                if (state.countdownInterval) clearInterval(state.countdownInterval);
                if (state.redirectTimeout) clearTimeout(state.redirectTimeout);
                if (state.redirectInterval) clearInterval(state.redirectInterval);
                this.closeModal(modalId);
            };
        }
        
        // PROFIL gomb - MÓDOSÍTVA: index.html-re irányít
        if (dashboardBtn) {
            dashboardBtn.onclick = () => {
                if (state.countdownInterval) clearInterval(state.countdownInterval);
                if (state.redirectTimeout) clearTimeout(state.redirectTimeout);
                if (state.redirectInterval) clearInterval(state.redirectInterval);
                
                if (state.isSuccess) {
                    // Átirányítás a FŐOLDALRA (index.html)
                    this.redirectAfterLogin(modalId);
                } else if (state.isError) {
                    // Újrapróbálkozás
                    this.showRetryModal(username);
                }
            };
        }

        // VISSZASZÁMLÁLÁS
        state.countdownInterval = this.startCountdown(modalId, state.countdown, () => {
            if (!state.isError) {
                // Ha nem volt hiba, szimuláljuk a sikeres bejelentkezést
                this.performLogin(form, username, modalId);
            }
        }, 'login');

        // ESC billentyű
        this.setupEscHandler(modalId, () => {
            if (closeBtn) closeBtn.click();
        });

        // Azonnal indítjuk a bejelentkezést
        setTimeout(() => {
            this.performLogin(form, username, modalId);
        }, 100);
    }

    showLoginSuccessState(modalId, username) {
        const state = this.modalStates.get(modalId);
        if (!state) return;
        
        state.isSuccess = true;
        state.isError = false;

        const modal = document.getElementById(modalId);
        if (!modal) return;

        // FEJLÉC MÓDOSÍTÁSA - UGYANAZ MINT A KIJELENTKEZÉS SIKERESÉNÉL
        const header = modal.querySelector('.combined-modal-header');
        if (header) {
            header.classList.remove('login');
            header.classList.add('success');
            header.querySelector('.combined-modal-title').innerHTML = 
                '<i class="bi bi-check-circle"></i> Sikeres bejelentkezés';
        }
        
        // FŐ SZÖVEG MÓDOSÍTÁSA - MÓDOSÍTVA (Ugyanaz a szín mint kijelentkezésnél)
        const mainText = document.getElementById(`main-text-${modalId}`);
        if (mainText) {
            mainText.textContent = 'Sikeres bejelentkezés!';
            mainText.style.color = '#28a745'; // UGYANAZ A ZÖLD, MINT KIJELENTKEZÉS NÉL
        }
        
        // IKON MÓDOSÍTÁSA - JÓVAL LÁTHATÓBB IKON
        const icon = modal.querySelector('.combined-icon');
        if (icon) {
            icon.classList.remove('login');
            icon.classList.add('success');
            // Sokkal jobban látható ikon - nagy, tiszta, pozitív
            icon.innerHTML = '<i class="bi bi-unlock-fill"></i>'; // Nyitott zár vagy

            icon.style.animation = 'none';
        }
        
        // VISSZASZÁMLÁLÁS MÓDOSÍTÁSA - MÓDOSÍTVA (Ugyanaz a stílus)
        const countdownEl = document.getElementById(`countdown-${modalId}`);
        const textEl = document.getElementById(`text-${modalId}`);
        
        if (countdownEl) {
            countdownEl.textContent = '✓';
            countdownEl.classList.add('success');
            countdownEl.style.color = '#28a745';
            countdownEl.style.animation = 'none';
        }
        
        // SZÖVEG MÓDOSÍTÁSA - UGYANAZ A STÍLUS MINT KIJELENTKEZÉS NÉL
        if (textEl) {
            textEl.textContent = `Sikeres bejelentkezés! Átirányítás a főoldalra ${state.redirectCountdown} másodperc múlva...`;
            textEl.classList.add('success');
            textEl.style.color = '#28a745';
        }
        
        // GOMBOK MÓDOSÍTÁSA - MÓDOSÍTVA
        const closeBtn = document.getElementById(`close-${modalId}`);
        const dashboardBtn = document.getElementById(`dashboard-${modalId}`);
        
        if (closeBtn) {
            closeBtn.innerHTML = '<i class="bi bi-arrow-return-left"></i> Vissza';
            closeBtn.classList.add('back');
        }
        
        if (dashboardBtn) {
            dashboardBtn.disabled = false;
            dashboardBtn.innerHTML = '<i class="bi bi-house-fill"></i> Főoldal';
            dashboardBtn.classList.remove('login');
            dashboardBtn.classList.add('success');
        }
        
        // USER INFÓ FRISSÍTÉSE
        const userInfo = document.getElementById(`user-info-${modalId}`);
        if (userInfo) {
            userInfo.innerHTML = `<span style="color: #28a745;">✓ Bejelentkezve mint:</span> <strong>${username}</strong>`;
        }

        // Valódi bejelentkezés - authManager frissítése
        if (window.authManager) {
            setTimeout(() => {
                authManager.loadUserData().then(() => {
                    authManager.updateUI();
                });
            }, 500);
        }

        // AUTOMATIKUS ÁTIRÁNYÍTÁS BEÁLLÍTÁSA
        state.redirectTimeout = setTimeout(() => {
            console.log('🔄 Automatikus átirányítás a főoldalra...');
            this.redirectAfterLogin(modalId);
        }, 3000); // 3 másodperc múlva

        // VISSZASZÁMLÁLÁS AZ AUTOMATIKUS ÁTIRÁNYÍTÁSHOZ
        state.redirectInterval = setInterval(() => {
            if (!this.activeModals.has(modalId)) {
                clearInterval(state.redirectInterval);
                return;
            }
            
            state.redirectCountdown--;
            
            if (textEl) {
                textEl.textContent = `Sikeres bejelentkezés! Átirányítás a főoldalra ${state.redirectCountdown} másodperc múlva...`;
            }
            
            if (state.redirectCountdown <= 0) {
                clearInterval(state.redirectInterval);
            }
        }, 1000);
    }

    showLoginErrorState(modalId, errorMessage) {
        const state = this.modalStates.get(modalId);
        if (!state) return;
        
        state.isSuccess = false;
        state.isError = true;

        const modal = document.getElementById(modalId);
        if (!modal) return;

        // FEJLÉC MÓDOSÍTÁSA
        const header = modal.querySelector('.combined-modal-header');
        if (header) {
            header.classList.remove('login');
            header.classList.add('error');
            header.querySelector('.combined-modal-title').innerHTML = 
                '<i class="bi bi-exclamation-triangle"></i> Bejelentkezési hiba';
        }
        
        // FŐ SZÖVEG MÓDOSÍTÁSA
        const mainText = document.getElementById(`main-text-${modalId}`);
        if (mainText) {
            mainText.textContent = errorMessage || 'Hibás felhasználónév vagy jelszó!';
            mainText.style.color = '#dc3545';
        }
        
        // IKON MÓDOSÍTÁSA
        const icon = modal.querySelector('.combined-icon');
        if (icon) {
            icon.classList.remove('login');
            icon.classList.add('error');
            icon.innerHTML = '<i class="bi bi-exclamation-triangle"></i>';
            icon.style.animation = 'none';
        }
        
        // VISSZASZÁMLÁLÁS MÓDOSÍTÁSA
        const countdownEl = document.getElementById(`countdown-${modalId}`);
        const textEl = document.getElementById(`text-${modalId}`);
        
        if (countdownEl) {
            countdownEl.textContent = '❌';
            countdownEl.classList.add('error');
            countdownEl.style.color = '#dc3545';
        }
        
        if (textEl) {
            textEl.textContent = 'Kérlek, ellenőrizd az adatokat!';
            textEl.classList.add('error');
        }
        
        // GOMBOK MÓDOSÍTÁSA - JAVÍTOTT: mindkét gomb bezárja a modalt
        const closeBtn = document.getElementById(`close-${modalId}`);
        const dashboardBtn = document.getElementById(`dashboard-${modalId}`);
        
        if (closeBtn) {
            closeBtn.innerHTML = '<i class="bi bi-x-circle"></i> Bezárás';
            closeBtn.onclick = () => {
                this.closeModal(modalId);
            };
        }
        
        if (dashboardBtn) {
            dashboardBtn.disabled = false;
            dashboardBtn.innerHTML = '<i class="bi bi-arrow-clockwise"></i> Újrapróbálkozás';
            dashboardBtn.classList.remove('btn-primary');
            dashboardBtn.classList.add('btn-warning');
            dashboardBtn.onclick = () => {
                this.closeModal(modalId);
                // 300ms késleltetés, hogy a modal teljesen bezáruljon
                setTimeout(() => {
                    // Fókusz a jelszó mezőre
                    const passwordInput = document.getElementById('UserPassword');
                    if (passwordInput) {
                        passwordInput.value = ''; // Töröljük a jelszót
                        passwordInput.focus(); // Fókusz a jelszó mezőre
                    }
                }, 300);
            };
        }
    }

    /* ========== KÖZÖS HELPER FUNKCIÓK ========== */
    insertModal(html) {
        const container = document.getElementById('combined-modal-container');
        if (container) {
            container.innerHTML += html;
        } else {
            document.body.innerHTML += html;
        }
    }

    activateModal(modalId) {
        const modalEl = document.getElementById(modalId);
        if (modalEl) {
            modalEl.classList.add('active');
            const modalContent = modalEl.querySelector('.combined-modal');
            if (modalContent) modalContent.classList.add('active');
        }
    }

    startCountdown(modalId, initialCount, onComplete, type = 'logout') {
        const state = this.modalStates.get(modalId);
        if (!state) return null;
        
        let countdown = initialCount;
        return setInterval(() => {
            if (!this.activeModals.has(modalId)) {
                return;
            }
            
            countdown--;
            
            const countdownEl = document.getElementById(`countdown-${modalId}`);
            const textEl = document.getElementById(`text-${modalId}`);
            
            if (countdownEl) {
                countdownEl.textContent = countdown;
                if (countdown <= 1) {
                    countdownEl.style.color = '#ff4757';
                }
            }
            
            if (textEl) {
                const actionText = type === 'logout' 
                    ? 'Automatikus kijelentkezés' 
                    : 'Sikeres bejelentkezés';
                textEl.innerHTML = `${actionText} <strong>${countdown}</strong> másodperc múlva...`;
            }
            
            if (countdown <= 0) {
                if (onComplete) onComplete();
            }
        }, 1000);
    }

    setupEscHandler(modalId, action) {
        const escHandler = (e) => {
            if (e.key === 'Escape' && this.activeModals.has(modalId)) {
                action();
            }
        };
        
        document.addEventListener('keydown', escHandler);
        this.currentEscHandler = escHandler;
    }

    showErrorModal(message) {
        const modalId = 'error-modal-' + Date.now();
        this.activeModals.add(modalId);

        const modalHtml = `
            <div class="combined-modal-overlay" id="${modalId}">
                <div class="combined-modal">
                    <div class="combined-modal-header error">
                        <h3 class="combined-modal-title">
                            <i class="bi bi-exclamation-circle"></i>
                            Hiba
                        </h3>
                    </div>
                    <div class="combined-modal-body">
                        <div class="combined-icon error">
                            <i class="bi bi-exclamation-circle"></i>
                        </div>
                        
                        <h4 style="margin-bottom: 1rem; font-size: 1.3rem;">
                            ${message}
                        </h4>
                    </div>
                    
                    <div class="combined-modal-footer">
                        <button class="combined-btn btn-cancel" id="ok-${modalId}" style="flex: none; min-width: 120px;">
                            OK
                        </button>
                    </div>
                </div>
            </div>
        `;

        this.insertModal(modalHtml);

        setTimeout(() => {
            this.activateModal(modalId);
        }, 50);

        const okBtn = document.getElementById(`ok-${modalId}`);
        if (okBtn) {
            okBtn.onclick = () => {
                this.closeModal(modalId);
            };
        }
    }

    showSimpleModal(message, title = 'Információ', autoHideSeconds = 3) {
        const modalId = 'simple-modal-' + Date.now();
        this.activeModals.add(modalId);

        const modalHtml = `
            <div class="combined-modal-overlay" id="${modalId}">
                <div class="combined-modal">
                    <div class="combined-modal-header">
                        <h3 class="combined-modal-title">
                            <i class="bi bi-info-circle"></i>
                            ${title}
                        </h3>
                    </div>
                    <div class="combined-modal-body">
                        ${message}
                    </div>
                    <div class="combined-modal-footer">
                        <button class="combined-btn btn-primary" id="ok-${modalId}">
                            OK
                        </button>
                    </div>
                </div>
            </div>
        `;

        this.insertModal(modalHtml);

        setTimeout(() => {
            this.activateModal(modalId);
        }, 50);

        const okBtn = document.getElementById(`ok-${modalId}`);
        if (okBtn) {
            okBtn.onclick = () => {
                this.closeModal(modalId);
            };
        }

        if (autoHideSeconds > 0) {
            setTimeout(() => {
                if (this.activeModals.has(modalId)) {
                    this.closeModal(modalId);
                }
            }, autoHideSeconds * 1000);
        }
    }

    showRetryModal(username) {
        const modalId = 'retry-modal-' + Date.now();
        this.activeModals.add(modalId);

        const modalHtml = `
            <div class="combined-modal-overlay" id="${modalId}">
                <div class="combined-modal">
                    <div class="combined-modal-header">
                        <h3 class="combined-modal-title">
                            <i class="bi bi-arrow-clockwise"></i>
                            Újrapróbálkozás
                        </h3>
                    </div>
                    <div class="combined-modal-body">
                        <div class="combined-icon">
                            <i class="bi bi-key"></i>
                        </div>
                        
                        <h4 style="margin-bottom: 1.5rem; font-size: 1.4rem;">
                            Új bejelentkezési kísérlet
                        </h4>
                        
                        <p style="color: var(--gray-400); font-size: 0.9rem; margin-top: 1.5rem;">
                            Ellenőrizd, hogy helyesen adtad meg a felhasználóneved és jelszavad.
                        </p>
                    </div>
                    
                    <div class="combined-modal-footer">
                        <button class="combined-btn btn-cancel" id="cancel-${modalId}">
                            <i class="bi bi-x"></i>
                            Mégse
                        </button>
                        
                        <button class="combined-btn btn-primary login" id="retry-${modalId}">
                            <i class="bi bi-arrow-clockwise"></i>
                            Bejelentkezés újra
                        </button>
                    </div>
                </div>
            </div>
        `;

        this.insertModal(modalHtml);

        setTimeout(() => {
            this.activateModal(modalId);
        }, 50);

        const cancelBtn = document.getElementById(`cancel-${modalId}`);
        const retryBtn = document.getElementById(`retry-${modalId}`);

        if (cancelBtn) {
            cancelBtn.onclick = () => {
                this.closeModal(modalId);
            };
        }

        if (retryBtn) {
            retryBtn.onclick = () => {
                this.closeModal(modalId);
                // Újra megjelenítjük a login modalt
                setTimeout(() => {
                    // Keresd meg a login formot
                    const loginForm = document.querySelector('#loginForm') || 
                                     document.querySelector('form[action*="login"]');
                    this.showLoginModal(username, loginForm);
                }, 300);
            };
        }
    }

    /* ========== ÁTIRÁNYÍTÁS FUNKCIÓK ========== */
    redirectToHome(modalId) {
        if (!this.activeModals.has(modalId)) return;
        
        if (this.currentEscHandler) {
            document.removeEventListener('keydown', this.currentEscHandler);
            this.currentEscHandler = null;
        }
        
        this.cleanupModalState(modalId);
        
        const modal = document.getElementById(modalId);
        if (modal) {
            modal.classList.remove('active');
            const modalContent = modal.querySelector('.combined-modal');
            if (modalContent) {
                modalContent.classList.remove('active');
            }
            
            setTimeout(() => {
                if (modal && modal.parentNode) {
                    modal.remove();
                }
                
                window.location.href = this.logoutRedirectUrl;
            }, 400);
        } else {
            window.location.href = this.logoutRedirectUrl;
        }
    }

    // Bejelentkezés utáni átirányítás
    redirectAfterLogin(modalId) {
        if (!this.activeModals.has(modalId)) return;
        
        if (this.currentEscHandler) {
            document.removeEventListener('keydown', this.currentEscHandler);
            this.currentEscHandler = null;
        }
        
        this.cleanupModalState(modalId);
        
        const modal = document.getElementById(modalId);
        if (modal) {
            modal.classList.remove('active');
            const modalContent = modal.querySelector('.combined-modal');
            if (modalContent) {
                modalContent.classList.remove('active');
            }
            
            setTimeout(() => {
                if (modal && modal.parentNode) {
                    modal.remove();
                }
                
                console.log(`📍 Bejelentkezés után átirányítás: ${this.loginRedirectUrl}`);
                window.location.href = this.loginRedirectUrl;
            }, 300);
        } else {
            console.log(`📍 Bejelentkezés után átirányítás: ${this.loginRedirectUrl}`);
            window.location.href = this.loginRedirectUrl;
        }
    }

    /* ========== KÖZÖS SEGÉDFUNKCIÓK ========== */
    cleanupModalState(modalId) {
        const state = this.modalStates.get(modalId);
        if (state) {
            if (state.countdownInterval) clearInterval(state.countdownInterval);
            if (state.redirectTimeout) clearTimeout(state.redirectTimeout);
            if (state.redirectInterval) clearInterval(state.redirectInterval);
            this.modalStates.delete(modalId);
        }
        this.activeModals.delete(modalId);
    }

    closeModal(modalId) {
        if (!this.activeModals.has(modalId)) return;
        
        if (this.currentEscHandler) {
            document.removeEventListener('keydown', this.currentEscHandler);
            this.currentEscHandler = null;
        }
        
        this.cleanupModalState(modalId);
        
        const modal = document.getElementById(modalId);
        if (modal) {
            modal.classList.remove('active');
            const modalContent = modal.querySelector('.combined-modal');
            if (modalContent) {
                modalContent.classList.remove('active');
            }
            
            setTimeout(() => {
                if (modal && modal.parentNode) {
                    modal.remove();
                }
            }, 400);
        }
    }

    // JAVÍTOTT: Session visszaállítása
    restoreSession() {
        console.log('🔄 Session visszaállítása');
        
        // 1. Ha van elmentett eredeti session ID, állítsuk vissza
        if (this.originalSessionId) {
            console.log('🔧 Eredeti session ID visszaállítása:', this.originalSessionId);
            
            // Session cookie visszaállítása
            const expiryDate = new Date();
            expiryDate.setDate(expiryDate.getDate() + 7); // 7 nap múlva lejár
            
            document.cookie = `SessionID=${this.originalSessionId}; path=/; expires=${expiryDate.toUTCString()}; SameSite=Strict`;
            
            // 2. Próbáljuk meg visszaállítani a sessiont a szerver oldalon is
            if (typeof $ !== 'undefined') {
                $.ajax({
                    type: "POST",
                    url: "/Session/RestoreSession",
                    data: { sessionId: this.originalSessionId },
                    timeout: 3000
                }).then((response) => {
                    console.log('✅ Session visszaállítva szerver oldalon:', response);
                }).catch((err) => {
                    console.warn('⚠️ Session visszaállítási hiba:', err);
                    // Fallback: próbáljuk meg újra bejelentkezni
                    this.tryReauth();
                });
            }
        } else {
            console.warn('⚠️ Nincs elmentett eredeti session ID');
            // Fallback: próbáljuk meg megtartani a jelenlegi sessiont
            const currentSessionId = this.getCookie('SessionID');
            if (!currentSessionId) {
                console.warn('⚠️ Nincs session cookie sem');
            }
        }
        
        // UI frissítés
        setTimeout(() => {
            console.log('✅ Session visszaállítva');
            // Frissítsük az authManager-t
            if (window.authManager) {
                authManager.loadUserData().then(() => {
                    authManager.updateUI();
                });
            }
        }, 500);
    }

    // Fallback: próbáljunk újra bejelentkezni
    tryReauth() {
        console.log('🔐 Újra hitelesítés próbálkozás');
        
        // Kérjük le a felhasználó adatokat
        if (typeof $ !== 'undefined') {
            $.ajax({
                type: "GET",
                url: "/Session/GetUserId",
                timeout: 3000
            }).then((response) => {
                if (response && response.isAuthenticated) {
                    console.log('✅ Felhasználó hitelesítve:', response.userName);
                } else {
                    console.warn('⚠️ Felhasználó nincs hitelesítve');
                }
            }).catch((err) => {
                console.error('❌ Hitelesítési hiba:', err);
            });
        }
    }

    getCookie(name) {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; ${name}=`);
        if (parts.length === 2) return parts.pop().split(';').shift();
        return null;
    }

    performLogin(form, username, modalId) {
        if (!form) {
            console.error('❌ Nincs login form');
            this.showLoginErrorState(modalId, 'Hiba a bejelentkezési űrlap betöltésekor');
            return;
        }

        const state = this.modalStates.get(modalId);
        if (!state) return;

        // Ha már sikeres vagy hibás volt, ne csináljunk semmit
        if (state.isSuccess || state.isError) return;

        // Jelszó mezők ellenőrzése
        const passwordInput = form.querySelector('input[type="password"], #UserPassword');
        const usernameInput = form.querySelector('input[type="text"], #UserName');
        
        if (!passwordInput || !usernameInput) {
            console.error('❌ Hiányzó bemeneti mezők');
            this.showLoginErrorState(modalId, 'Hiányzó bemeneti mezők');
            return;
        }

        const loginData = {
            UserName: usernameInput.value,
            UserPassword: passwordInput.value
        };

        console.log('🔐 Bejelentkezési adatok:', { 
            username: loginData.UserName, 
            passwordLength: loginData.UserPassword ? loginData.UserPassword.length : 0 
        });

        // VALÓDI API HIVÁS
        $.ajax({
            type: "POST",
            url: "/Session/Login",
            contentType: "application/x-www-form-urlencoded; charset=UTF-8",
            data: loginData,
            success: (response) => {
                console.log('✅ Bejelentkezési API válasz:', response);
                
                // Ellenőrizzük a választ
                if (response && (response.success === true || response.userName)) {
                    this.showLoginSuccessState(modalId, response.userName || username);
                } else {
                    const errorMsg = response.message || response.error || 'Hibás bejelentkezési adatok';
                    this.showLoginErrorState(modalId, errorMsg);
                }
            },
            error: (xhr, status, error) => {
                console.error('❌ Bejelentkezési API hiba:', { 
                    status: xhr.status, 
                    statusText: xhr.statusText,
                    responseText: xhr.responseText,
                    error: error 
                });
                
                let errorMessage = 'Szerver hiba a bejelentkezés során';
                
                if (xhr.status === 401) {
                    errorMessage = 'Hibás felhasználónév vagy jelszó';
                } else if (xhr.status === 500) {
                    errorMessage = 'Szerver hiba (500). Kérlek, próbáld újra később.';
                } else if (xhr.responseText) {
                    // Próbáljuk meg kinyerni az üzenetet a válaszból
                    try {
                        const errorResponse = JSON.parse(xhr.responseText);
                        errorMessage = errorResponse.message || errorResponse.error || errorMessage;
                    } catch (e) {
                        // Nem JSON válasz
                        errorMessage = xhr.responseText.substring(0, 100) + '...';
                    }
                }
                
                this.showLoginErrorState(modalId, errorMessage);
            },
            complete: () => {
                // Clear the countdown interval when API call completes
                if (state.countdownInterval) {
                    clearInterval(state.countdownInterval);
                    state.countdownInterval = null;
                }
            }
        });
    }
}

// INICIALIZÁLÁS
document.addEventListener('DOMContentLoaded', () => {
    if (!document.querySelector('link[href*="bootstrap-icons"]')) {
        const iconLink = document.createElement('link');
        iconLink.rel = 'stylesheet';
        iconLink.href = 'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css';
        document.head.appendChild(iconLink);
    }
    
    window.combinedModal = new CombinedModal();
    console.log('✨ CombinedModal készen áll');
});
