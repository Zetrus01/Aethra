// js/smart-cart-manager.js - TELJESEN MŰKÖDŐ OFFLINE-KÉPES VERZIÓ
// JAVÍTVA: Egységes guest ID kezelés az oldalak között
class SmartCartManager {
    constructor() {
        this.cartItems = [];
        this.userReservations = [];
        this.userId = null;
        this.isInitialized = false;
        this.pendingUserSwitch = null;
        
        console.log("🛒 SmartCartManager példány létrehozva");
        
        setTimeout(() => this.initialize(), 50);
    }

    async initialize() {
        console.log("🔧 SmartCartManager inicializálás indítása...");
        
        if (!window.cartManager) {
            window.cartManager = this;
            console.log("🌐 SmartCartManager beállítva globálisan");
        }
        
        const hasAuthManager = await this.waitForAuthManager();
        
        if (hasAuthManager && window.authManager.getAuthState) {
            await this.initializeWithAuthManager();
        } else {
            console.log("👤 Guest user inicializálás");
            this.initializeAsGuest();
        }
        
        this.setupAuthManagerConnection();
        this.finalizeInitialization();
    }

    async waitForAuthManager() {
        return new Promise((resolve) => {
            let attempts = 0;
            const maxAttempts = 20;
            
            const checkAuth = () => {
                attempts++;
                
                if (window.authManager) {
                    console.log("✅ AuthManager megtalálva");
                    resolve(true);
                } else if (attempts >= maxAttempts) {
                    console.log("⏳ AuthManager nem található");
                    resolve(false);
                } else {
                    setTimeout(checkAuth, 100);
                }
            };
            
            checkAuth();
        });
    }

    async initializeWithAuthManager() {
        try {
            const authState = window.authManager.getAuthState();
            console.log("🔍 Auth státusz:", authState);
            
            if (authState.isAuthenticated && authState.userData) {
                this.userId = authState.userData.userId || authState.userData.userName;
                console.log("👤 Bejelentkezett user kosara:", this.userId);
            } else {
                console.log("👤 Guest user inicializálás");
                this.initializeAsGuest();
            }
            
        } catch (error) {
            console.error("❌ Hiba az AuthManager használatakor:", error);
            this.initializeAsGuest();
        }
    }

    // JAVÍTVA: Egységes guest ID kezelés localStorage-ból
    initializeAsGuest() {
        // Ellenőrizzük, hogy van-e már guest ID a localStorage-ban
        const existingGuestId = localStorage.getItem('aethra_guest_id');
        
        if (existingGuestId) {
            // Ha van, használjuk azt
            this.userId = existingGuestId;
            console.log("👤 Meglévő guest ID használata:", this.userId);
        } else {
            // Ha nincs, generáljunk újat
            this.userId = 'guest_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
            localStorage.setItem('aethra_guest_id', this.userId);
            console.log("👤 Új guest ID generálva:", this.userId);
        }
        
        // Backup a régi kulccsal is (kompatibilitás miatt)
        localStorage.setItem('currentGuestId', this.userId);
    }

    setupAuthManagerConnection() {
        if (window.authManager && window.authManager.setCartManager) {
            console.log("🔗 CartManager beállítása az AuthManager-ben");
            window.authManager.setCartManager(this);
        } else {
            console.log("⏳ AuthManager még nem elérhető, későbbi csatolás...");
            this.retryAuthManagerConnection();
        }
    }

    retryAuthManagerConnection() {
        let attempts = 0;
        const maxAttempts = 30;
        
        const tryConnect = () => {
            attempts++;
            
            if (window.authManager && window.authManager.setCartManager) {
                console.log("🔗 CartManager utólagos beállítása az AuthManager-ben");
                window.authManager.setCartManager(this);
                return;
            }
            
            if (attempts < maxAttempts) {
                setTimeout(tryConnect, 200);
            } else {
                console.log("ℹ️ AuthManager nem lett elérhető a várt időn belül");
            }
        };
        
        setTimeout(tryConnect, 500);
    }

    finalizeInitialization() {
        this.loadUserData();
        this.isInitialized = true;
        
        console.log("✅ SmartCartManager sikeresen inicializálva userrel:", this.userId);
        console.log("📊 Kosár állapot:", this.getDebugInfo());
        
        window.dispatchEvent(new CustomEvent('cartManagerReady', {
            detail: {
                userId: this.userId,
                isInitialized: true,
                cartData: this.getCartData()
            }
        }));

        if (this.pendingUserSwitch) {
            console.log("🔄 Függőben lévő user váltás végrehajtása:", this.pendingUserSwitch);
            this.setUserId(this.pendingUserSwitch);
            this.pendingUserSwitch = null;
        }
    }

    // JAVÍTVA: createGuestId most már csak a localStorage-ból olvas, nem generál újat
    createGuestId() {
        const existingGuestId = localStorage.getItem('aethra_guest_id');
        if (existingGuestId) {
            return existingGuestId;
        }
        
        const guestId = 'guest_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
        localStorage.setItem('aethra_guest_id', guestId);
        localStorage.setItem('currentGuestId', guestId);
        return guestId;
    }

    setUserId(newUserId) {
        if (!this.isInitialized) {
            console.log("⏳ CartManager még nincs inicializálva, user váltás késleltetve:", newUserId);
            this.pendingUserSwitch = newUserId;
            return;
        }
        
        if (this.userId === newUserId) {
            console.log("ℹ️ User ID változatlan:", newUserId);
            return;
        }
        
        console.log("🔄 User váltás a kosárban:", this.userId, "→", newUserId);
        
        this.saveUserData();
        const oldUserId = this.userId;
        this.userId = newUserId;
        
        // Ha guest user, mentsük el a localStorage-ba
        if (newUserId && newUserId.startsWith('guest_')) {
            localStorage.setItem('aethra_guest_id', newUserId);
            localStorage.setItem('currentGuestId', newUserId);
        }
        
        this.loadUserData();
        
        console.log("✅ User váltás sikeres. Régi user:", oldUserId, "Új user:", newUserId);
        
        this.triggerCartUpdate();
        this.triggerReservationUpdate();
        
        if (window.authManager && window.authManager.isAuthenticated) {
            setTimeout(() => {
                this.syncPendingReservations();
            }, 1000);
        }
    }

    loadUserData() {
        try {
            if (!this.userId) {
                console.error("❌ User ID nincs beállítva");
                this.cartItems = [];
                this.userReservations = [];
                return;
            }
            
            const userCartKey = `cart_${this.userId}`;
            const savedCart = localStorage.getItem(userCartKey);
            this.cartItems = savedCart ? JSON.parse(savedCart) : [];
            
            const userReservationsKey = `reservations_${this.userId}`;
            const savedReservations = localStorage.getItem(userReservationsKey);
            this.userReservations = savedReservations ? JSON.parse(savedReservations) : [];
            
            console.log("📁 Adatok betöltve usernek:", this.userId, 
                "-", this.cartItems.length + " kosár elem", 
                "-", this.userReservations.length + " foglalás");
            
            this.updateCartCounter();
            
        } catch (error) {
            console.error("❌ Hiba az adatok betöltésekor:", error);
            this.cartItems = [];
            this.userReservations = [];
        }
    }

    saveUserData() {
        try {
            if (!this.userId) {
                console.error("❌ User ID nincs beállítva");
                return;
            }
            
            const userCartKey = `cart_${this.userId}`;
            localStorage.setItem(userCartKey, JSON.stringify(this.cartItems));
            
            const userReservationsKey = `reservations_${this.userId}`;
            localStorage.setItem(userReservationsKey, JSON.stringify(this.userReservations));
            
            console.log("💾 Adatok mentve usernek:", this.userId);
        } catch (error) {
            console.error("❌ Hiba az adatok mentésekor:", error);
        }
    }

    // Dátum/idő formázó segédfüggvények
    formatDateForAPI(dateString) {
        if (!dateString) return '';
        
        if (dateString.match(/^\d{4}-\d{2}-\d{2}$/)) {
            return dateString;
        }
        
        const match = dateString.match(/(\d{4})[\.\s]*(\d{1,2})[\.\s]*(\d{1,2})/);
        if (match) {
            const year = match[1];
            const month = match[2].padStart(2, '0');
            const day = match[3].padStart(2, '0');
            return `${year}-${month}-${day}`;
        }
        
        console.warn('⚠️ Nem sikerült formázni a dátumot:', dateString);
        return dateString;
    }

    formatTimeForAPI(timeString) {
        if (!timeString) return '';
        
        if (timeString.match(/^\d{2}:\d{2}$/)) {
            return timeString;
        }
        
        const match = timeString.match(/(\d{1,2}):(\d{2})/);
        if (match) {
            const hour = match[1].padStart(2, '0');
            const minute = match[2];
            return `${hour}:${minute}`;
        }
        
        if (timeString.match(/^\d{1,2}\.$/)) {
            console.warn('⚠️ Hibás idő formátum:', timeString);
            return '00:00';
        }
        
        console.warn('⚠️ Nem sikerült formázni az időt:', timeString);
        return timeString;
    }

    // ASZTALFOGLALÁS KEZELÉSE - OFFLINE-KÉPES
    async addReservation(reservationData) {
        if (!this.isInitialized) {
            throw new Error('CartManager még nincs inicializálva');
        }
        
        if (!this.userId) {
            throw new Error('User ID nincs beállítva');
        }
        
        console.log('📅 Asztalfoglalás hozzáadása:', reservationData.tableName, 'User:', this.userId);
        console.log('📅 Nyers foglalás adatok:', reservationData);
        
        // Ellenőrizzük, hogy van-e már aktív foglalás
        const activeReservation = this.getActiveReservation();
        if (activeReservation) {
            throw new Error('Már van egy aktív asztalfoglalásod');
        }
        
        // Dátum/idő normalizálás a mentés előtt
        const normalizedData = { ...reservationData };
        
        // Biztosítjuk, hogy az originalTime és originalDate legyen
        if (!normalizedData.originalTime && normalizedData.time) {
            normalizedData.originalTime = normalizedData.time;
            console.log('🔄 originalTime beállítva:', normalizedData.originalTime);
        }
        
        if (!normalizedData.originalDate && normalizedData.date) {
            normalizedData.originalDate = normalizedData.date;
            console.log('🔄 originalDate beállítva:', normalizedData.originalDate);
        }
        
        // Hibás formátumok javítása
        if (normalizedData.time && normalizedData.time.match(/^\d{1,2}\.$/)) {
            console.log('🔄 Time mező javítása:', normalizedData.time, '→', normalizedData.originalTime);
            normalizedData.time = normalizedData.originalTime || normalizedData.time;
        }
        
        if (normalizedData.date && normalizedData.date.match(/^\d{4}\.$/)) {
            console.log('🔄 Date mező javítása:', normalizedData.date, '→', normalizedData.originalDate);
            normalizedData.date = normalizedData.originalDate || normalizedData.date;
        }
        
        // ID generálás
        const reservationId = 'reservation_' + Date.now();
        const newReservation = {
            ...normalizedData,
            reservationId: reservationId,
            userId: this.userId,
            createdAt: new Date().toISOString(),
            status: 'active',
            syncedWithDb: false,
            needsSync: true  // Jelöljük, hogy szinkronizálásra vár
        };
        
        console.log('📅 Normalizált foglalás adatok:', {
            date: newReservation.date,
            time: newReservation.time,
            originalTime: newReservation.originalTime,
            originalDate: newReservation.originalDate,
            tableNumber: newReservation.tableNumber,
            tableName: newReservation.tableName
        });
        
        // Hozzáadjuk a foglalásokhoz
        this.userReservations.push(newReservation);
        
        // Mentés a localStorage-ba
        this.saveUserData();
        this.triggerReservationUpdate();
        
        console.log('✅ Asztalfoglalás sikeresen rögzítve a localStorage-ban:', reservationId);
        
        // Próbáljuk meg szinkronizálni az adatbázissal (ha be van jelentkezve)
        const shouldTrySync = window.authManager && window.authManager.isAuthenticated && navigator.onLine;
        
        if (shouldTrySync) {
            try {
                console.log('🔄 Szinkronizálás indítása (online állapot)...');
                const syncResult = await this.syncReservationWithDatabase(newReservation);
                
                if (syncResult.success) {
                    console.log('✅ Asztalfoglalás szinkronizálva az adatbázissal:', syncResult.dbReservationId);
                    newReservation.dbReservationId = syncResult.dbReservationId;
                    newReservation.syncedWithDb = true;
                    newReservation.needsSync = false;
                    this.saveUserData();
                    this.triggerReservationUpdate();
                } else if (syncResult.offline) {
                    console.log('📴 Offline mód, csak localStorage-ban mentve');
                    newReservation.syncError = syncResult.message;
                    newReservation.needsSync = true;
                    this.saveUserData();
                }
            } catch (syncError) {
                console.warn('⚠️ Asztalfoglalás szinkronizálása sikertelen:', syncError.message);
                newReservation.syncError = syncError.message;
                newReservation.needsSync = true;
                this.saveUserData();
            }
        } else {
            const reason = !window.authManager?.isAuthenticated ? 'nincs bejelentkezve' : 'offline állapot';
            console.log(`👤 ${reason}, csak localStorage-ban mentve`);
            newReservation.needsSync = true;
        }
        
        return {
            success: true,
            message: "Asztalfoglalás sikeres",
            reservationId: reservationId,
            reservation: newReservation,
            synced: newReservation.syncedWithDb || false
        };
    }
    
    getActiveReservation() {
        if (!this.userId || this.userReservations.length === 0) {
            return null;
        }
        
        const activeReservations = this.userReservations
            .filter(r => r.status === 'active')
            .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
        
        return activeReservations.length > 0 ? activeReservations[0] : null;
    }

    getReservationDetails() {
        const activeReservation = this.getActiveReservation();
        if (!activeReservation) {
            return null;
        }
        
        // Normalizáljuk a dátum és idő mezőket a megjelenítéshez
        let displayTime = activeReservation.originalTime || activeReservation.time;
        let displayDate = activeReservation.originalDate || activeReservation.date;
        
        // Idő formátum javítása
        if (displayTime && displayTime.includes('.')) {
            const timeMatch = displayTime.match(/(\d{1,2}):(\d{2})/);
            if (timeMatch) {
                displayTime = timeMatch[0];
            } else if (displayTime.match(/^\d{1,2}\.$/)) {
                console.warn('⚠️ Hibás idő formátum a foglalásban:', displayTime);
                displayTime = activeReservation.originalTime || '00:00';
            }
        }
        
        // Dátum formátum javítása
        if (displayDate && displayDate.match(/^\d{4}\.$/)) {
            console.warn('⚠️ Hibás dátum formátum a foglalásban:', displayDate);
            displayDate = activeReservation.originalDate || displayDate;
        }
        
        return {
            reservationId: activeReservation.reservationId,
            dbReservationId: activeReservation.dbReservationId,
            tableName: activeReservation.tableName,
            tableNumber: activeReservation.tableNumber,
            date: displayDate,
            time: displayTime,
            originalTime: activeReservation.originalTime,
            originalDate: activeReservation.originalDate,
            guests: activeReservation.guests,
            tableLocation: activeReservation.tableLocation,
            message: activeReservation.message,
            tableId: activeReservation.tableId,
            createdAt: activeReservation.createdAt,
            status: activeReservation.status,
            syncedWithDb: activeReservation.syncedWithDb || false,
            orderId: activeReservation.orderId,
            needsSync: activeReservation.needsSync || false,
            syncError: activeReservation.syncError,
            extraServices: activeReservation.extraServices
        };
    }
    
    async cancelReservation(reservationId = null) {
        if (!this.isInitialized) {
            throw new Error('CartManager még nincs inicializálva');
        }
        
        let reservationToCancel;
        
        if (reservationId) {
            reservationToCancel = this.userReservations.find(r => r.reservationId === reservationId);
        } else {
            reservationToCancel = this.getActiveReservation();
        }
        
        if (!reservationToCancel) {
            throw new Error('Nincs aktív asztalfoglalás');
        }
        
        reservationToCancel.status = 'cancelled';
        reservationToCancel.cancelledAt = new Date().toISOString();
        
        this.saveUserData();
        this.triggerReservationUpdate();
        
        console.log('🗑️ Asztalfoglalás lemondva:', reservationToCancel.reservationId);
        
        // Ha van adatbázis ID és online állapotban vagyunk, töröljük az adatbázisból is
        if (reservationToCancel.dbReservationId && window.authManager && 
            window.authManager.isAuthenticated && navigator.onLine) {
            try {
                await this.deleteReservationFromDatabase(reservationToCancel.dbReservationId);
                console.log('✅ Asztalfoglalás törölve az adatbázisból:', reservationToCancel.dbReservationId);
            } catch (dbError) {
                console.warn('⚠️ Asztalfoglalás adatbázisból való törlése sikertelen:', dbError.message);
                reservationToCancel.needsSync = true;
                reservationToCancel.syncError = dbError.message;
                this.saveUserData();
            }
        }
        
        return {
            success: true,
            message: "Asztalfoglalás sikeresen lemondva",
            cancelledOffline: !reservationToCancel.dbReservationId || !navigator.onLine
        };
    }
    
    getUserReservations() {
        return {
            active: this.getActiveReservation(),
            all: this.userReservations,
            userId: this.userId
        };
    }
    
    triggerReservationUpdate() {
        const activeReservation = this.getActiveReservation();
        
        window.dispatchEvent(new CustomEvent('reservationUpdated', {
            detail: {
                hasReservation: !!activeReservation,
                reservation: activeReservation,
                reservationDetails: this.getReservationDetails(),
                userId: this.userId
            }
        }));
        
        console.log('🔔 Asztalfoglalás frissítés esemény küldve');
    }

    // FONTOS: OFFLINE-KÉPES státusz frissítés
    async markReservationAsOrdered(reservationId, orderId = null) {
        if (!this.isInitialized) {
            throw new Error('CartManager még nincs inicializálva');
        }
        
        if (!this.userId) {
            throw new Error('User ID nincs beállítva');
        }
        
        console.log('🔄 Asztalfoglalás státusz frissítése "ordered"-re:', reservationId, 'OrderId:', orderId);
        
        const reservationIndex = this.userReservations.findIndex(r => r.reservationId === reservationId);
        if (reservationIndex === -1) {
            throw new Error('Asztalfoglalás nem található');
        }
        
        // Státusz frissítése a localStorage-ban
        this.userReservations[reservationIndex].status = 'ordered';
        
        if (orderId) {
            this.userReservations[reservationIndex].orderId = orderId;
        }
        
        // Mentés a localStorage-ba
        this.saveUserData();
        this.triggerReservationUpdate();
        
        console.log('✅ Asztalfoglalás státusza frissítve "ordered"-re (localStorage):', reservationId);
        
        // Próbáljuk meg szinkronizálni az adatbázissal (ha online vagyunk és be van jelentkezve)
        const shouldTrySync = window.authManager && window.authManager.isAuthenticated && navigator.onLine;
        
        if (shouldTrySync) {
            try {
                const updateResult = await this.updateReservationStatusInDatabase(
                    reservationId, 
                    'ordered', 
                    orderId
                );
                
                if (updateResult.success) {
                    console.log('✅ Asztalfoglalás státusza frissítve az adatbázisban:', reservationId);
                    
                    if (updateResult.dbReservationId) {
                        this.userReservations[reservationIndex].dbReservationId = updateResult.dbReservationId;
                        this.userReservations[reservationIndex].syncedWithDb = true;
                        this.userReservations[reservationIndex].needsSync = false;
                        delete this.userReservations[reservationIndex].syncError;
                        this.saveUserData();
                    }
                } else {
                    console.warn('⚠️ Adatbázis frissítés részlegesen sikertelen:', updateResult.message);
                    this.userReservations[reservationIndex].needsSync = true;
                    this.userReservations[reservationIndex].syncError = updateResult.message;
                    this.saveUserData();
                }
            } catch (updateError) {
                console.warn('⚠️ Asztalfoglalás adatbázis státusz frissítése sikertelen:', updateError.message);
                this.userReservations[reservationIndex].needsSync = true;
                this.userReservations[reservationIndex].syncError = updateError.message;
                this.saveUserData();
            }
        } else {
            console.log('📴 Offline mód, csak localStorage-ban frissítve');
            this.userReservations[reservationIndex].needsSync = true;
            this.userReservations[reservationIndex].syncError = 'Offline mód, későbbi szinkronizálás';
            this.saveUserData();
        }
        
        return {
            success: true,
            message: "Asztalfoglalás státusza frissítve",
            reservation: this.userReservations[reservationIndex],
            synced: !this.userReservations[reservationIndex].needsSync
        };
    }
    
    // OFFLINE-KÉPES szinkronizálás
    async syncReservationWithDatabase(reservationData) {
        try {
            console.log('🔄 Asztalfoglalás szinkronizálása az adatbázissal:', reservationData.reservationId);
            
            // Ellenőrizzük, hogy be van-e jelentkezve és online vagyunk-e
            if (!window.authManager || !window.authManager.isAuthenticated) {
                console.log('ℹ️ Nincs bejelentkezve, offline mentés');
                return {
                    success: false,
                    message: "Nincs bejelentkezve, offline mentés",
                    offline: true
                };
            }
            
            if (!navigator.onLine) {
                console.log('🌐 Offline állapot, későbbi szinkronizálás');
                return {
                    success: false,
                    message: "Offline állapot, későbbi szinkronizálás",
                    offline: true
                };
            }
            
            // Dátum és idő formázása API számára
            const formattedDate = this.formatDateForAPI(reservationData.date || reservationData.originalDate);
            const formattedTime = this.formatTimeForAPI(reservationData.time || reservationData.originalTime);
            
            if (!formattedDate || !formattedTime) {
                console.warn('⚠️ Hiányzó dátum vagy idő, offline mentés');
                return {
                    success: false,
                    message: "Hiányzó dátum vagy idő, offline mentés",
                    offline: true
                };
            }
            
            console.log('📡 API hívás indítása...');
            console.log('📦 Küldött adatok:', {
                TableName: reservationData.tableName || '',
                TableNumber: String(reservationData.tableNumber || '0'),
                TableLocation: reservationData.tableLocation || '',
                Date: formattedDate,
                Time: formattedTime,
                Guests: reservationData.guests || 1,
                Message: reservationData.message || '',
                LocalReservationId: reservationData.reservationId
            });
            
            // CSRF token keresése
            let csrfToken = '';
            if (window.authManager.getCSRFToken) {
                csrfToken = window.authManager.getCSRFToken();
            } else if (window.authManager.getCookie) {
                csrfToken = window.authManager.getCookie('CSRF-TOKEN');
            }
            
            if (!csrfToken) {
                csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                if (!csrfToken) {
                    csrfToken = document.cookie.match(/CSRF-TOKEN=([^;]+)/)?.[1];
                }
            }
            
            const headers = {
                'Content-Type': 'application/json'
            };
            
            if (csrfToken) {
                headers['RequestVerificationToken'] = csrfToken;
                console.log('✅ CSRF token hozzáadva');
            } else {
                console.warn('⚠️ CSRF token nélkül küldjük');
            }
            
            const apiData = {
                TableName: reservationData.tableName || '',
                TableNumber: String(reservationData.tableNumber || '0'),
                TableLocation: reservationData.tableLocation || '',
                Date: formattedDate,
                Time: formattedTime,
                Guests: reservationData.guests || 1,
                Message: reservationData.message || '',
                LocalReservationId: reservationData.reservationId
            };
            
            const response = await fetch('/Reservation/CreateReservation', {
                method: 'POST',
                headers: headers,
                body: JSON.stringify(apiData),
                credentials: 'include'
            });
            
            console.log('📡 API válasz státusz:', response.status, response.statusText);
            
            if (!response.ok) {
                const errorText = await response.text();
                console.error('❌ API hiba részletek:', errorText.substring(0, 200));
                
                try {
                    const errorJson = JSON.parse(errorText);
                    throw new Error(`API hiba ${response.status}: ${errorJson.title || errorJson.message || 'Validációs hiba'}`);
                } catch {
                    throw new Error(`API hiba ${response.status}: ${errorText.substring(0, 100)}`);
                }
            }
            
            const result = await response.json();
            console.log('✅ API válasz:', result);
            
            if (result.success) {
                console.log('✅ Asztalfoglalás sikeresen szinkronizálva az adatbázisban:', result.reservationId);
                
                return {
                    success: true,
                    dbReservationId: result.reservationId,
                    message: "Asztalfoglalás szinkronizálva az adatbázisban"
                };
            } else {
                throw new Error(result.message || 'Ismeretlen hiba az API válaszból');
            }
            
        } catch (error) {
            console.warn('⚠️ API hiba, de offline folytatjuk:', error.message);
            return {
                success: false,
                message: `API hiba: ${error.message}`,
                offline: true
            };
        }
    }
    
    async updateReservationStatusInDatabase(reservationId, status, orderId = null) {
        try {
            console.log(`🔄 Asztalfoglalás státusz frissítése adatbázisban: ${reservationId} -> ${status}`);
            
            // Ellenőrizzük, hogy be van-e jelentkezve és online vagyunk-e
            if (!window.authManager || !window.authManager.isAuthenticated) {
                throw new Error('Nincs bejelentkezve, nem lehet frissíteni');
            }
            
            if (!navigator.onLine) {
                throw new Error('Offline állapot, későbbi szinkronizálás');
            }
            
            // Először keressük meg a foglalást
            const reservation = this.userReservations.find(r => r.reservationId === reservationId);
            if (!reservation) {
                throw new Error('Foglalás nem található');
            }
            
            // Ha nincs dbReservationId, először szinkronizálnunk kell
            if (!reservation.dbReservationId) {
                console.log('🔄 Foglalás még nincs az adatbázisban, először szinkronizálás...');
                const syncResult = await this.syncReservationWithDatabase(reservation);
                
                if (!syncResult.success || !syncResult.dbReservationId) {
                    throw new Error('Nem sikerült szinkronizálni a foglalást: ' + (syncResult.message || 'Ismeretlen hiba'));
                }
                
                reservation.dbReservationId = syncResult.dbReservationId;
                reservation.syncedWithDb = true;
                this.saveUserData();
                console.log('✅ Foglalás sikeresen szinkronizálva, DB ID:', syncResult.dbReservationId);
            }
            
            // CSRF token keresése
            let csrfToken = '';
            if (window.authManager.getCSRFToken) {
                csrfToken = window.authManager.getCSRFToken();
            } else if (window.authManager.getCookie) {
                csrfToken = window.authManager.getCookie('CSRF-TOKEN');
            }
            
            const headers = {
                'Content-Type': 'application/json'
            };
            
            if (csrfToken) {
                headers['RequestVerificationToken'] = csrfToken;
            }
            
            const response = await fetch('/Reservation/UpdateReservationStatus', {
                method: 'POST',
                headers: headers,
                body: JSON.stringify({
                    ReservationId: reservation.dbReservationId,
                    Status: status,
                    OrderId: orderId
                })
            });
            
            if (!response.ok) {
                throw new Error(`API hiba: ${response.status}`);
            }
            
            const result = await response.json();
            
            if (result.success) {
                console.log('✅ Asztalfoglalás státusza frissítve az adatbázisban:', reservation.dbReservationId);
                return {
                    success: true,
                    dbReservationId: reservation.dbReservationId,
                    message: "Asztalfoglalás státusza frissítve az adatbázisban",
                    reservation: result.reservation
                };
            } else {
                throw new Error(result.message || 'Ismeretlen hiba');
            }
            
        } catch (error) {
            console.warn('⚠️ Adatbázis frissítés hiba:', error.message);
            return {
                success: false,
                message: `Adatbázis frissítési hiba: ${error.message}`
            };
        }
    }
    
    async deleteReservationFromDatabase(dbReservationId) {
        try {
            console.log('🗑️ Asztalfoglalás törlése az adatbázisból:', dbReservationId);
            
            if (!window.authManager || !window.authManager.isAuthenticated) {
                throw new Error('Nincs bejelentkezve, nem lehet törölni');
            }
            
            if (!navigator.onLine) {
                throw new Error('Offline állapot, későbbi szinkronizálás');
            }
            
            let csrfToken = '';
            if (window.authManager.getCSRFToken) {
                csrfToken = window.authManager.getCSRFToken();
            } else if (window.authManager.getCookie) {
                csrfToken = window.authManager.getCookie('CSRF-TOKEN');
            }
            
            const headers = {};
            if (csrfToken) {
                headers['RequestVerificationToken'] = csrfToken;
            }
            
            const response = await fetch(`/Reservation/DeleteReservation/${dbReservationId}`, {
                method: 'DELETE',
                headers: headers
            });
            
            if (!response.ok) {
                throw new Error(`API hiba: ${response.status}`);
            }
            
            const result = await response.json();
            
            if (result.success) {
                console.log('✅ Asztalfoglalás sikeresen törölve az adatbázisból:', dbReservationId);
                return {
                    success: true,
                    message: "Asztalfoglalás törölve az adatbázisból"
                };
            } else {
                throw new Error(result.message || 'Ismeretlen hiba');
            }
            
        } catch (error) {
            console.error('❌ Hiba az asztalfoglalás törlésekor:', error.message);
            return {
                success: false,
                message: `Törlési hiba: ${error.message}`
            };
        }
    }
    
    async getActiveReservationFromDatabase() {
        try {
            console.log('📡 Aktív asztalfoglalás lekérése az adatbázisból');
            
            if (!window.authManager || !window.authManager.isAuthenticated) {
                throw new Error('Nincs bejelentkezve');
            }
            
            if (!navigator.onLine) {
                throw new Error('Offline állapot');
            }
            
            const response = await fetch('/Reservation/GetActiveReservation');
            
            if (!response.ok) {
                throw new Error(`API hiba: ${response.status}`);
            }
            
            const result = await response.json();
            
            if (result.success && result.hasActiveReservation) {
                console.log('✅ Aktív asztalfoglalás betöltve az adatbázisból:', result.reservation.ReservationId);
                return {
                    success: true,
                    hasActiveReservation: true,
                    reservation: result.reservation
                };
            } else {
                console.log('ℹ️ Nincs aktív asztalfoglalás az adatbázisban');
                return {
                    success: true,
                    hasActiveReservation: false
                };
            }
            
        } catch (error) {
            console.error('❌ Hiba az aktív foglalás lekérdezésekor:', error.message);
            return {
                success: false,
                message: `Lekérdezési hiba: ${error.message}`
            };
        }
    }

    // AUTOMATIKUS OFFLINE SZINKRONIZÁLÁS
    async syncPendingReservations() {
        if (!window.authManager || !window.authManager.isAuthenticated) {
            console.log('ℹ️ Nincs bejelentkezve, szinkronizálás kihagyva');
            return;
        }
        
        if (!navigator.onLine) {
            console.log('🌐 Offline állapot, szinkronizálás később');
            return;
        }
        
        const pendingReservations = this.userReservations.filter(
            r => (r.needsSync && !r.syncedWithDb && r.status === 'active') || 
                 (r.syncError && r.status === 'active')
        );
        
        if (pendingReservations.length === 0) {
            console.log('ℹ️ Nincs függőben lévő szinkronizálandó foglalás');
            return;
        }
        
        console.log(`🔄 ${pendingReservations.length} függőben lévő foglalás szinkronizálása...`);
        
        let successfulSyncs = 0;
        
        for (const reservation of pendingReservations) {
            try {
                console.log(`🔄 Szinkronizálás: ${reservation.reservationId} - ${reservation.tableName}`);
                const syncResult = await this.syncReservationWithDatabase(reservation);
                
                if (syncResult.success) {
                    reservation.dbReservationId = syncResult.dbReservationId;
                    reservation.syncedWithDb = true;
                    reservation.needsSync = false;
                    delete reservation.syncError;
                    successfulSyncs++;
                    console.log(`✅ Foglalás szinkronizálva: ${reservation.reservationId}`);
                } else if (syncResult.offline) {
                    console.log(`📴 Offline módban marad: ${reservation.reservationId}`);
                }
            } catch (error) {
                console.warn(`⚠️ Foglalás szinkronizálása sikertelen: ${reservation.reservationId}`, error.message);
                reservation.syncError = error.message;
            }
        }
        
        if (successfulSyncs > 0) {
            this.saveUserData();
            this.triggerReservationUpdate();
        }
        
        console.log(`✅ Szinkronizálás befejezve: ${successfulSyncs}/${pendingReservations.length} sikeres`);
    }

    // KOSÁR KEZELÉS
    async addToCart(item) {
        if (!this.isInitialized) {
            throw new Error('CartManager még nincs inicializálva');
        }
        
        console.log('📥 Kosárba helyezés:', item.name, 'User:', this.userId);
        
        if (item.type === 'reservation' || item.reservationData) {
            console.warn('⚠️ Asztalfoglalás nem kerülhet a kosárba');
            throw new Error('Asztalfoglalást külön kell kezelni');
        }
        
        if (!item.name || item.quantity < 1) {
            throw new Error('Érvénytelen termék adatok');
        }

        const existingItem = this.cartItems.find(cartItem => 
            cartItem.name === item.name && 
            cartItem.date === item.date && 
            cartItem.time === item.time &&
            cartItem.consumption === item.consumption
        );
        
        if (existingItem) {
            existingItem.quantity += item.quantity;
            console.log(`📈 Mennyiség növelve: ${item.name} → ${existingItem.quantity}`);
        } else {
            this.cartItems.push({
                ...item,
                itemId: Date.now().toString(),
                userId: this.userId,
                addedAt: new Date().toISOString(),
                type: 'food'
            });
            console.log(`✅ Új elem: ${item.name}`);
        }

        this.saveUserData();
        this.updateCartCounter();
        this.triggerCartUpdate();
        
        return {
            success: true,
            message: "Termék hozzáadva a kosárhoz",
            itemCount: this.cartItems.length,
            totalQuantity: this.getTotalQuantity()
        };
    }

    async updateQuantity(itemId, newQuantity) {
        if (!this.isInitialized) {
            console.error('❌ CartManager még nincs inicializálva');
            return;
        }

        if (newQuantity < 1) {
            await this.removeItem(itemId);
            return;
        }

        const item = this.cartItems.find(item => item.itemId === itemId);
        if (!item) {
            console.error('❌ Termék nem található:', itemId);
            return;
        }

        item.quantity = newQuantity;
        this.saveUserData();
        this.updateCartCounter();
        this.triggerCartUpdate();
        
        console.log(`✏️ Mennyiség módosítva: ${item.name} → ${newQuantity}`);
    }

    async removeItem(itemId) {
        if (!this.isInitialized) {
            console.error('❌ CartManager még nincs inicializálva');
            return;
        }

        const itemIndex = this.cartItems.findIndex(item => item.itemId === itemId);
        if (itemIndex === -1) {
            console.error('❌ Termék nem található a törléshez:', itemId);
            return;
        }

        const removedItem = this.cartItems[itemIndex];
        this.cartItems.splice(itemIndex, 1);
        this.saveUserData();
        this.updateCartCounter();
        this.triggerCartUpdate();
        
        console.log(`🗑️ Törölve: ${removedItem.name}`);
    }

    triggerCartUpdate() {
        const eventDetail = { 
            itemCount: this.getTotalQuantity(), 
            items: this.cartItems,
            userId: this.userId,
            cartData: this.getCartData()
        };
        
        window.dispatchEvent(new CustomEvent('cartUpdated', {
            detail: eventDetail
        }));
        
        console.log('🔔 Kosár frissítés esemény küldve');
    }

    updateCartCounter() {
        const totalItems = this.getTotalQuantity();
        const cartCounter = document.querySelector('.cart-counter');
        
        if (cartCounter) {
            cartCounter.textContent = totalItems > 99 ? '99+' : totalItems.toString();
            cartCounter.style.display = totalItems > 0 ? 'flex' : 'none';
        }
    }

    getTotalQuantity() {
        return this.cartItems.reduce((total, item) => total + item.quantity, 0);
    }

    getCartData() {
        if (!this.isInitialized) {
            console.warn('⚠️ CartManager még nincs inicializálva');
            return {
                items: [],
                totalItems: 0,
                totalPrice: 0,
                userId: null,
                reservation: this.getActiveReservation(),
                reservationDetails: this.getReservationDetails()
            };
        }
        
        const cartData = {
            items: this.cartItems,
            totalItems: this.getTotalQuantity(),
            totalPrice: this.cartItems.reduce((total, item) => total + (item.price * item.quantity), 0),
            userId: this.userId,
            reservation: this.getActiveReservation(),
            reservationDetails: this.getReservationDetails()
        };
        
        return cartData;
    }

    clearCart() {
        if (!this.isInitialized) {
            console.error('❌ CartManager még nincs inicializálva');
            return;
        }
        
        this.cartItems = [];
        this.saveUserData();
        this.updateCartCounter();
        this.triggerCartUpdate();
        
        console.log('🧹 Kosár kiürítve');
    }

    waitForInitialization() {
        return new Promise((resolve, reject) => {
            if (this.isInitialized) {
                resolve();
                return;
            }
            
            const timeout = setTimeout(() => {
                reject(new Error('CartManager inicializálás időtúllépés'));
            }, 10000);
            
            window.addEventListener('cartManagerReady', () => {
                clearTimeout(timeout);
                resolve();
            });
        });
    }

    getDebugInfo() {
        const pendingSyncCount = this.userReservations.filter(
            r => r.needsSync && !r.syncedWithDb
        ).length;
        
        const onlineStatus = navigator.onLine ? 'online' : 'offline';
        const authStatus = window.authManager?.isAuthenticated ? 'authenticated' : 'not authenticated';
        
        return {
            userId: this.userId,
            cartItems: this.cartItems.length,
            totalItems: this.getTotalQuantity(),
            isInitialized: this.isInitialized,
            userReservations: this.userReservations.length,
            activeReservation: !!this.getActiveReservation(),
            pendingSyncCount: pendingSyncCount,
            onlineStatus: onlineStatus,
            authStatus: authStatus,
            storageKeys: {
                cart: this.userId ? `cart_${this.userId}` : null,
                reservations: this.userId ? `reservations_${this.userId}` : null
            },
            guestIdSource: this.userId?.startsWith('guest_') ? 'localStorage' : 'authenticated',
            storedGuestId: localStorage.getItem('aethra_guest_id')
        };
    }
}

// Globális példány létrehozása
console.log("🛒 SmartCartManager betöltése...");

// Online/offline eseményfigyelő a szinkronizáláshoz
function setupOnlineOfflineListeners() {
    window.addEventListener('online', () => {
        console.log('🌐 Online állapot - szinkronizálás indítása...');
        setTimeout(() => {
            if (window.cartManager && window.authManager && window.authManager.isAuthenticated) {
                window.cartManager.syncPendingReservations();
            }
        }, 2000);
    });
    
    window.addEventListener('offline', () => {
        console.log('📴 Offline állapot - offline módban folytatjuk');
    });
}

// Betöltési esemény
window.dispatchEvent(new CustomEvent('cartManagerLoading'));

// Online/offline listener beállítása
setupOnlineOfflineListeners();

// Globális példány létrehozása (csak ha még nem létezik)
if (!window.cartManager) {
    setTimeout(() => {
        if (!window.cartManager) {
            window.cartManager = new SmartCartManager();
            console.log("✅ SmartCartManager példány létrehozva és elérhető");
        }
    }, 100);
} else {
    console.log('ℹ️ CartManager már létezik, új példány nem jön létre');
}

// Exportálás globálisan
window.SmartCartManager = SmartCartManager;