// auth.js - VÉGLEGES JAVÍTOTT VERZIÓ (konstruktorban inicializálással)

class AuthManager {
    constructor() {
        this.userData = null;
        this.isAuthenticated = false;
        this.cartManager = null;
        this.cartManagerConnectionAttempts = 0;
        this.maxConnectionAttempts = 30;
        this.uiUpdateAttempted = false;
        this.pendingUserSwitch = null;
        this.initialized = false;
        
        // 🔧 AZONNALI INICIALIZÁLÁS - mindig lesz permissionManager
        console.log("🔧 PermissionManager azonnali létrehozása...");
        this.permissionManager = new PermissionManager(this);
        this.permissionManager.setDefaultPermissions();
        
        console.log("🔐 AuthManager példány létrehozva - szerepkör:", this.permissionManager.userRoles);
    }

    setCartManager(cartManager) {
        this.cartManager = cartManager;
        this.cartManagerConnectionAttempts = 0;
        console.log('🔗 CartManager beállítva az AuthManager-ben');
        
        if (this.pendingUserSwitch) {
            console.log('🔄 Függőben lévő user váltás végrehajtása:', this.pendingUserSwitch);
            this.cartManager.setUserId(this.pendingUserSwitch);
            this.pendingUserSwitch = null;
        } else {
            this.performUserSwitchIfNeeded();
        }
    }

    async loadUserData() {
        console.log("🔍 loadUserData meghívva");
        
        try {
            const response = await $.ajax({
                type: "GET",
                url: "/Session/GetUserId",
                headers: { 
                    'RequestVerificationToken': this.getCookie('CSRF-TOKEN') 
                },
                timeout: 5000
            });

            if (response && response.userName) {
                this.userData = response;
                this.isAuthenticated = true;
                console.log("✅ Felhasználó betöltve:", response.userName);
                
                console.log("🔑 Jogosultságok frissítése bejelentkezett felhasználóhoz...");
                await this.refreshPermissions();
                
                this.performUserSwitchIfNeeded();
                return response;
            }
        } catch (error) {
            console.log("🔍 loadUserData catch ág - error:", error.status, error.statusText);
            
            if (error.status === 401) {
                console.log("ℹ️ Felhasználó nincs bejelentkezve (401-es hiba)");
                this.isAuthenticated = false;
                this.userData = null;
            } else {
                console.error("❌ Hiba a felhasználó betöltésekor:", error);
                this.isAuthenticated = false;
                this.userData = null;
            }
            
            // 🔧 GUEST JOGOSULTSÁGOK BEÁLLÍTÁSA
            console.log("🔑 Guest jogosultságok beállítása...");
            await this.refreshPermissions();
        }
        
        return null;
    }
    
    // Jogosultságok frissítése (nem hoz létre újat, csak frissíti a meglévőt)
    async refreshPermissions() {
        console.log("🔑 refreshPermissions meghívva - isAuthenticated:", this.isAuthenticated);
        
        if (this.isAuthenticated && this.userData) {
            console.log("👤 Bejelentkezett felhasználó jogosultságainak betöltése:", this.userData.userName);
            await this.permissionManager.loadUserPermissions(this.userData.userId);
        } else {
            console.log("👤 Guest jogosultságok beállítása");
            this.permissionManager.setDefaultPermissions();
        }
        
        console.log("✅ Jogosultságok frissítve:", this.permissionManager.userRoles);
        console.log("🔑 Jogosultságok listája:", this.permissionManager.userPermissions);
        
        // Permission esemény küldése
        window.dispatchEvent(new CustomEvent('permissionsLoaded', {
            detail: {
                roles: this.permissionManager.userRoles,
                permissions: this.permissionManager.userPermissions,
                isAuthenticated: this.isAuthenticated
            }
        }));
    }
    
    // JOGOSULTSÁG ELLENŐRZŐ METÓDUSOK
    hasPermission(permission) {
        if (!this.permissionManager) {
            console.warn("⚠️ permissionManager még nem inicializálva, false-t adok vissza");
            return false;
        }
        return this.permissionManager.hasPermission(permission);
    }
    
    hasRole(role) {
        if (!this.permissionManager) {
            console.warn("⚠️ permissionManager még nem inicializálva, false-t adok vissza");
            return false;
        }
        return this.permissionManager.hasRole(role);
    }
    
    hasAllPermissions(permissions) {
        return this.permissionManager?.hasAllPermissions(permissions) || false;
    }
    
    hasAnyPermission(permissions) {
        return this.permissionManager?.hasAnyPermission(permissions) || false;
    }
    
    protectPageElements() {
        this.permissionManager?.protectPageElements();
    }

    performUserSwitchIfNeeded() {
        if (this.isAuthenticated && this.userData) {
            const userId = this.userData.userId || this.userData.userName;
            console.log('🔄 AuthManager: Bejelentkezett user váltás -', userId);

            if (!this.cartManager || !this.cartManager.isInitialized) {
                console.log('⏳ CartManager még nem elérhető/inicializált, váltás késleltetve...');
                this.pendingUserSwitch = userId;
                this.retryCartManagerConnection();
                return;
            }

            try {
                console.log('🎯 CartManager user váltás (bejelentkezett):', userId);
                this.cartManager.setUserId(userId);
                console.log('✅ User váltás sikeresen indítva');
            } catch (error) {
                console.error('❌ Hiba a user váltás közben:', error);
            }
        } else {
            console.log('👤 AuthManager: Guest user állapot, nincs szükség váltásra');
            
            if (this.cartManager && this.cartManager.isInitialized) {
                const guestId = this.cartManager.userId;
                console.log('✅ CartManager guest módban:', guestId);
                
                const storedGuestId = localStorage.getItem('aethra_guest_id');
                if (storedGuestId && guestId !== storedGuestId) {
                    console.warn('⚠️ Guest ID eltérés! Frissítés szükséges');
                    this.cartManager.setUserId(storedGuestId);
                }
            }
        }
    }

    retryCartManagerConnection() {
        if (this.cartManagerConnectionAttempts >= this.maxConnectionAttempts) {
            console.warn('⏹️ Maximális próbálkozások száma elérve');
            return;
        }

        this.cartManagerConnectionAttempts++;
        console.log(`🔄 CartManager kapcsolat próbálkozás: ${this.cartManagerConnectionAttempts}/${this.maxConnectionAttempts}`);

        if (window.cartManager) {
            console.log('✅ CartManager megtalálva!');
            
            if (window.cartManager.isInitialized) {
                console.log('✅ CartManager már inicializálva, csatolás...');
                this.setCartManager(window.cartManager);
                
                if (this.pendingUserSwitch) {
                    console.log('🔄 Függőben lévő user váltás végrehajtása csatolás után:', this.pendingUserSwitch);
                    window.cartManager.setUserId(this.pendingUserSwitch);
                    this.pendingUserSwitch = null;
                }
            } else {
                console.log('⏳ CartManager még nincs inicializálva, várakozás...');
                
                const checkInit = setInterval(() => {
                    if (window.cartManager && window.cartManager.isInitialized) {
                        clearInterval(checkInit);
                        console.log('✅ CartManager most lett inicializálva, csatolás...');
                        this.setCartManager(window.cartManager);
                        
                        if (this.pendingUserSwitch) {
                            console.log('🔄 Függőben lévő user váltás végrehajtása inicializálás után:', this.pendingUserSwitch);
                            window.cartManager.setUserId(this.pendingUserSwitch);
                            this.pendingUserSwitch = null;
                        }
                    }
                }, 100);
                
                setTimeout(() => {
                    clearInterval(checkInit);
                    if (!window.cartManager?.isInitialized) {
                        console.warn('⚠️ CartManager nem inicializálódott időben');
                    }
                }, 5000);
            }
            return;
        }

        setTimeout(() => {
            this.retryCartManagerConnection();
        }, 300);
    }

    async logout() {
        try {
            await $.ajax({
                type: "POST",
                url: "/Session/Logout",
                headers: { 
                    'RequestVerificationToken': this.getCookie('CSRF-TOKEN') 
                },
                timeout: 3000
            });
            
            this.isAuthenticated = false;
            this.userData = null;
            
            await this.refreshPermissions();
            
            console.log("✅ Sikeres kijelentkezés");
            this.updateUI();
            
            setTimeout(() => {
                window.location.href = 'index.html';
            }, 500);
            
            return true;
            
        } catch (error) {
            console.error("❌ Hiba a kijelentkezéskor:", error);
            return false;
        }
    }

    getCookie(name) {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; ${name}=`);
        if (parts.length === 2) return parts.pop().split(';').shift();
        return null;
    }

    getAuthState() {
        return {
            isAuthenticated: this.isAuthenticated,
            userData: this.userData,
            pendingUserSwitch: this.pendingUserSwitch,
            cartManagerConnected: !!this.cartManager,
            cartManagerInitialized: this.cartManager?.isInitialized || false,
            guestId: localStorage.getItem('aethra_guest_id'),
            roles: this.permissionManager?.userRoles || ['guest'],
            permissions: this.permissionManager?.userPermissions || []
        };
    }

    getCSRFToken() {
        return this.getCookie('CSRF-TOKEN') || 
               document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    }

    startSessionWatcher(interval = 300000) {
        setInterval(async () => {
            await this.loadUserData();
            this.updateUI();
        }, interval);
    }

    updateUI() {
        const roles = this.permissionManager?.userRoles?.join(', ') || 'guest';
        console.log(`🔄 UI frissítése - Bejelentkezve: ${this.isAuthenticated}, Felhasználó: ${this.userData?.userName || 'Guest'}, Szerepkörök: ${roles}`);
        
        if (window.navbar && window.navbar.navbarLoaded) {
            window.navbar.updateAuthUI(this.isAuthenticated, this.userData?.userName);
        } else {
            console.warn('⚠️ Navbar még nincs betöltve, késleltetett UI frissítés...');
            
            if (!this.uiUpdateAttempted) {
                this.uiUpdateAttempted = true;
                setTimeout(() => {
                    console.log('🔄 Késleltetett UI frissítés...');
                    if (window.navbar && window.navbar.navbarLoaded) {
                        window.navbar.updateAuthUI(this.isAuthenticated, this.userData?.userName);
                    }
                    this.uiUpdateAttempted = false;
                }, 1500);
            }
        }
        
        setTimeout(() => {
            this.protectPageElements();
        }, 200);
    }

    async initialize() {
        if (this.initialized) {
            console.log('ℹ️ AuthManager már inicializálva');
            return;
        }
        
        console.log('🔐 AuthManager inicializálása...');
        try {
            await this.loadUserData();
            this.retryCartManagerConnection();
            this.updateUI();
            this.startSessionWatcher();
            
            this.initialized = true;
            console.log('✅ AuthManager sikeresen inicializálva');
            
            window.dispatchEvent(new CustomEvent('authManagerReady', {
                detail: this.getAuthState()
            }));
            
        } catch (error) {
            console.error('❌ Hiba az AuthManager inicializálásakor:', error);
        }
    }

    forceConnectCartManager() {
        console.log('🔧 Kényszerített CartManager csatolás...');
        this.cartManagerConnectionAttempts = 0;
        this.retryCartManagerConnection();
    }

    getDebugInfo() {
        return {
            isAuthenticated: this.isAuthenticated,
            userData: this.userData,
            cartManager: !!this.cartManager,
            cartManagerInitialized: this.cartManager?.isInitialized || false,
            cartManagerUserId: this.cartManager?.userId || null,
            pendingUserSwitch: this.pendingUserSwitch,
            connectionAttempts: this.cartManagerConnectionAttempts,
            maxAttempts: this.maxConnectionAttempts,
            initialized: this.initialized,
            storedGuestId: localStorage.getItem('aethra_guest_id'),
            roles: this.permissionManager?.userRoles || [],
            permissions: this.permissionManager?.userPermissions || []
        };
    }
}


// PERMISSION MANAGER OSZTÁLY

class PermissionManager {
    constructor(authManager) {
        this.authManager = authManager;
        this.userRoles = ['guest'];
        this.userPermissions = [];
        
        // Szerepkör hierarchia (öröklődés) - csak a 3 szerepkörhöz
        this.roleHierarchy = {
            'admin': ['user', 'guest'],
            'user': ['guest'],
            'guest': []
        };
        
        console.log("🔑 PermissionManager létrehozva");
    }
    
    async loadUserPermissions(userId) {
        try {
            // 1. Lekérjük a szerepköröket
            const rolesResponse = await $.ajax({
                type: "GET",
                url: "/Session/GetUserRoles",
                headers: { 'RequestVerificationToken': this.authManager.getCSRFToken() },
                timeout: 3000
            });
            
            console.log("📥 GetUserRoles válasz:", rolesResponse);
            
            if (rolesResponse && rolesResponse.roles && Array.isArray(rolesResponse.roles)) {
                this.userRoles = rolesResponse.roles;
            } else {
                throw new Error("Invalid roles response");
            }
            
            // 2. Lekérjük a jogosultságokat
            const permissionsResponse = await $.ajax({
                type: "GET",
                url: "/Session/GetUserPermissions",
                headers: { 'RequestVerificationToken': this.authManager.getCSRFToken() },
                timeout: 3000
            });
            
            console.log("📥 GetUserPermissions válasz:", permissionsResponse);
            
            if (permissionsResponse && permissionsResponse.permissions && Array.isArray(permissionsResponse.permissions)) {
                this.userPermissions = permissionsResponse.permissions;
            } else {
                // Fallback: szerepkörök alapján számoljuk a jogosultságokat
                this.userPermissions = this.collectPermissionsFromRoles(this.userRoles);
            }
            
            console.log('✅ Jogosultságok betöltve szerverről:', this.userRoles);
            console.log('🔑 Jogosultságok:', this.userPermissions);
            return true;
            
        } catch (error) {
            console.error('❌ Szerverről nem sikerült betölteni:', error);
            this.setDefaultPermissions();
            return false;
        }
    }
    
    collectPermissionsFromRoles(roles) {
        // Csak 3 szerepkörre szabott permissionMap
        const permissionMap = {
            'admin': [
                'product.view', 'product.create', 'product.edit', 'product.delete',
                'order.view_own', 'order.view_all', 'order.create', 'order.edit', 'order.delete', 'order.update_status',
                'reservation.create', 'reservation.view_own', 'reservation.view_all', 'reservation.edit', 'reservation.delete', 'reservation.manage_all',
                'cart.view', 'cart.edit',
                'profile.view', 'profile.edit'
            ],
            'user': [
                'product.view',
                'order.view_own', 'order.create',
                'reservation.view_own', 'reservation.create',
                'cart.view', 'cart.edit',
                'profile.view', 'profile.edit'
            ],
            'guest': [
                'product.view',
                'reservation.create',
                'cart.view', 'cart.edit'
            ]
        };
        
        const permissions = new Set();
        
        for (const role of roles) {
            const perms = permissionMap[role] || permissionMap['guest'];
            perms.forEach(p => permissions.add(p));
            
            // Öröklődés a hierarchia alapján
            if (this.roleHierarchy[role]) {
                for (const inheritedRole of this.roleHierarchy[role]) {
                    const inheritedPerms = permissionMap[inheritedRole] || [];
                    inheritedPerms.forEach(p => permissions.add(p));
                }
            }
        }
        
        return Array.from(permissions);
    }
    
    setDefaultPermissions() {
        console.log("🔧 setDefaultPermissions hívva");
        
        if (this.authManager.isAuthenticated && this.authManager.userData) {
            const userName = this.authManager.userData.userName || '';
            if (userName === 'admin') {
                this.userRoles = ['admin'];
            } else {
                this.userRoles = ['user'];
            }
        } else {
            this.userRoles = ['guest'];
        }
        
        this.userPermissions = this.collectPermissionsFromRoles(this.userRoles);
        console.log('ℹ️ Alapértelmezett jogosultságok beállítva:', this.userRoles);
        console.log('🔑 Jogosultságok:', this.userPermissions);
    }
    
    hasPermission(permission) {
        return this.userPermissions.includes(permission);
    }
    
    hasRole(role) {
        if (this.userRoles.includes(role)) return true;
        
        // Hierarchia ellenőrzés
        for (const userRole of this.userRoles) {
            if (this.roleHierarchy[userRole]?.includes(role)) return true;
        }
        return false;
    }
    
    hasAnyRole(roles) {
        return roles.some(role => this.hasRole(role));
    }
    
    hasAnyPermission(permissions) {
        return permissions.some(p => this.hasPermission(p));
    }
    
    hasAllPermissions(permissions) {
        return permissions.every(p => this.hasPermission(p));
    }
    
    protectElement(elementId, requiredPermission) {
        const element = document.getElementById(elementId);
        if (element && !this.hasPermission(requiredPermission)) {
            element.style.display = 'none';
            return false;
        }
        return true;
    }
    
    protectPageElements() {
        // data-permission attribútum alapján
        document.querySelectorAll('[data-permission]').forEach(el => {
            const perm = el.getAttribute('data-permission');
            if (!this.hasPermission(perm)) {
                el.style.display = 'none';
                console.log(`🔒 Elem elrejtve (szükséges jogosultság: ${perm})`);
            } else {
                console.log(`✅ Elem látható (van jogosultság: ${perm})`);
            }
        });
        
        // data-role attribútum alapján
        document.querySelectorAll('[data-role]').forEach(el => {
            const role = el.getAttribute('data-role');
            if (!this.hasRole(role)) {
                el.style.display = 'none';
                console.log(`🔒 Elem elrejtve (szükséges szerepkör: ${role})`);
            } else {
                console.log(`✅ Elem látható (van szerepkör: ${role})`);
            }
        });
        
        // data-any-permission attribútum (bármelyik jogosultság elég)
        document.querySelectorAll('[data-any-permission]').forEach(el => {
            const perms = el.getAttribute('data-any-permission').split(',');
            if (!this.hasAnyPermission(perms)) {
                el.style.display = 'none';
                console.log(`🔒 Elem elrejtve (szükséges jogosultságok egyike: ${perms.join(', ')})`);
            }
        });
        
        // data-all-permissions attribútum (minden jogosultság kell)
        document.querySelectorAll('[data-all-permissions]').forEach(el => {
            const perms = el.getAttribute('data-all-permissions').split(',');
            if (!this.hasAllPermissions(perms)) {
                el.style.display = 'none';
                console.log(`🔒 Elem elrejtve (szükséges jogosultságok mindegyike: ${perms.join(', ')})`);
            }
        });
    }
}

// Globális instance
window.authManager = new AuthManager();

// Automatikus inicializálás oldalbetöltéskor
$(document).ready(function() {
    console.log('📄 AuthManager automatikus inicializálás...');
    
    setTimeout(() => {
        window.authManager.initialize();
    }, 200);
});

// SmartCartManager esemény figyelése
window.addEventListener('cartManagerReady', function(event) {
    console.log('🎯 CartManagerReady esemény - automatikus csatolás...', event.detail);
    if (window.authManager) {
        window.authManager.forceConnectCartManager();
    }
});

// PERMISSION ESEMÉNY FIGYELÉSE
window.addEventListener('permissionsLoaded', function(event) {
    console.log('🎯 PermissionsLoaded esemény - jogosultságok betöltve:', event.detail);
    
    setTimeout(() => {
        if (window.authManager) {
            window.authManager.protectPageElements();
            
            // Admin gomb külön kezelése
            const adminBtn = document.getElementById('closeDayBtn');
            if (adminBtn) {
                const hasAdminPermission = window.authManager.hasPermission('reservation.manage_all') ||
                                          window.authManager.hasRole('admin');
                if (!hasAdminPermission) {
                    adminBtn.style.display = 'none';
                    console.log("🔒 Admin gomb elrejtve (nincs jogosultság)");
                } else {
                    adminBtn.style.display = 'flex';
                    console.log("✅ Admin gomb megjelenítve (van jogosultság)");
                }
            }
        }
    }, 100);
});

console.log('🔐 AuthManager betöltve és készen áll');