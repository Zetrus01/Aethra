CREATE TABLE IF NOT EXISTS User (
    UserName TEXT PRIMARY KEY,
    Email TEXT NOT NULL UNIQUE,
    UserPassword TEXT NOT NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS UserAuth(
    UserName TEXT PRIMARY KEY,
    UserSalt TEXT NOT NULL,
    UserHash TEXT NOT NULL,
    FOREIGN KEY (UserName) REFERENCES User(UserName) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Session (
    SessionID TEXT PRIMARY KEY,
    CreationTime INTEGER NOT NULL,
    ExpiryTime INTEGER NOT NULL,
    UserName TEXT NOT NULL,
    FOREIGN KEY (UserName) REFERENCES User(UserName) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS Reservations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReservationId TEXT UNIQUE NOT NULL,
    UserId TEXT NOT NULL,
    TableName TEXT NOT NULL,
    TableNumber TEXT,
    TableLocation TEXT,
    Date TEXT NOT NULL,           -- YYYY-MM-DD format
    Time TEXT NOT NULL,           -- HH:MM format (e.g., "14:00") 
    Guests INTEGER NOT NULL,
    Status TEXT DEFAULT 'active',
    OrderId TEXT,
    Message TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES User(UserName)
);
ALTER TABLE Reservations ADD COLUMN ExtraServices TEXT;
CREATE INDEX IF NOT EXISTS idx_reservations_user ON Reservations(UserId);
CREATE INDEX IF NOT EXISTS idx_reservations_date ON Reservations(Date);
CREATE INDEX IF NOT EXISTS idx_reservations_order ON Reservations(OrderId);
CREATE INDEX IF NOT EXISTS idx_orders_user ON Orders(UserId);
CREATE INDEX IF NOT EXISTS idx_orders_date ON Orders(OrderDate);
CREATE INDEX IF NOT EXISTS idx_orderitems_order ON OrderItems(OrderId);
CREATE INDEX IF NOT EXISTS idx_invoices_user ON Invoices(UserId);
CREATE INDEX IF NOT EXISTS idx_invoices_order ON Invoices(OrderId);
CREATE INDEX IF NOT EXISTS idx_invoices_status ON Invoices(Status);
-- Rendelések tábla
CREATE TABLE IF NOT EXISTS Orders (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId TEXT UNIQUE NOT NULL,
    UserId TEXT NOT NULL,
    UserName TEXT NOT NULL,
    OrderDate TEXT NOT NULL,
    TotalPrice INTEGER NOT NULL,
    Status TEXT NOT NULL DEFAULT 'pending',
    ServiceFee INTEGER NOT NULL DEFAULT 0,
    ItemsCount INTEGER NOT NULL,
    ReservationId TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES User(UserName),
    FOREIGN KEY (ReservationId) REFERENCES Reservations(ReservationId)
);
ALTER TABLE Orders ADD COLUMN PaymentMethod TEXT DEFAULT 'card';
ALTER TABLE Orders ADD COLUMN DeliveryAddress TEXT;

-- Rendelt tételek - JAVÍTVA CreatedAt oszloppal
CREATE TABLE IF NOT EXISTS OrderItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId TEXT NOT NULL,
    ItemName TEXT NOT NULL,
    ItemDescription TEXT,
    Quantity INTEGER NOT NULL,
    UnitPrice INTEGER NOT NULL,
    TotalPrice INTEGER NOT NULL,
    ConsumptionType TEXT DEFAULT 'restaurant',
    ReservationDate TEXT,
    ReservationTime TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (OrderId) REFERENCES Orders(OrderId) ON DELETE CASCADE
);

-- Számlázás tábla - ÚJ
CREATE TABLE IF NOT EXISTS Invoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNumber TEXT UNIQUE NOT NULL,
    OrderId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    IssueDate TEXT NOT NULL,
    DueDate TEXT,
    TotalAmount INTEGER NOT NULL,
    TaxAmount INTEGER DEFAULT 0,
    Status TEXT DEFAULT 'unpaid',
    PaymentMethod TEXT,
    PaymentDate TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (OrderId) REFERENCES Orders(OrderId),
    FOREIGN KEY (UserId) REFERENCES User(UserName)
);
--Zártkörű napok
CREATE TABLE ClosedDays (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Date TEXT NOT NULL UNIQUE,
    Reason TEXT,
    ClosedBy TEXT,
    IsActive INTEGER DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,
    ReopenedAt TEXT,
    ReopenedBy TEXT
)

-- Indexek létrehozása
CREATE INDEX IF NOT EXISTS IX_Reviews_UserId ON Reviews(UserId);
CREATE INDEX IF NOT EXISTS IX_Reviews_Date ON Reviews(Date);
CREATE INDEX IF NOT EXISTS IX_Reviews_Rating ON Reviews(Rating);
CREATE INDEX IF NOT EXISTS IX_Reviews_IsActive ON Reviews(IsActive);

-- Indeksek a gyorsabb lekérdezésekhez
CREATE INDEX IF NOT EXISTS idx_reservations_user ON Reservations(UserId);
CREATE INDEX IF NOT EXISTS idx_reservations_date ON Reservations(Date);
CREATE INDEX IF NOT EXISTS idx_reservations_order ON Reservations(OrderId);
CREATE INDEX IF NOT EXISTS idx_orders_user ON Orders(UserId);
CREATE INDEX IF NOT EXISTS idx_orders_date ON Orders(OrderDate);
CREATE INDEX IF NOT EXISTS idx_orderitems_order ON OrderItems(OrderId);
CREATE INDEX IF NOT EXISTS idx_invoices_user ON Invoices(UserId);
CREATE INDEX IF NOT EXISTS idx_invoices_order ON Invoices(OrderId);
CREATE INDEX IF NOT EXISTS idx_invoices_status ON Invoices(Status);

-- Adatbázis verzió követés
CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version INTEGER PRIMARY KEY,
    AppliedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

INSERT OR IGNORE INTO SchemaVersion (Version) VALUES (1);

-- IsAdmin mező hozzáadása 
ALTER TABLE User ADD COLUMN IsAdmin INTEGER DEFAULT 0;

-- Admin felhasználó beállítása 
UPDATE User SET IsAdmin = 1 WHERE UserName = 'admin';


-- RBAC TÁBLÁK HOZZÁADÁSA 

-- Szerepkörök tábla
CREATE TABLE IF NOT EXISTS Roles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    DisplayName TEXT NOT NULL,
    Description TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Jogosultságok tábla
CREATE TABLE IF NOT EXISTS Permissions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    DisplayName TEXT NOT NULL,
    Category TEXT,
    Description TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Szerepkör-Jogosultság kapcsolat (M:N)
CREATE TABLE IF NOT EXISTS RolePermissions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RoleId INTEGER NOT NULL,
    PermissionId INTEGER NOT NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE,
    FOREIGN KEY (PermissionId) REFERENCES Permissions(Id) ON DELETE CASCADE,
    UNIQUE(RoleId, PermissionId)
);

-- Felhasználó-Szerepkör kapcsolat (M:N)
CREATE TABLE IF NOT EXISTS UserRoles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL,
    RoleId INTEGER NOT NULL,
    AssignedBy TEXT,
    AssignedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES User(UserName) ON DELETE CASCADE,
    FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE,
    UNIQUE(UserId, RoleId)
);


-- ALAPADATOK BESZÚRÁSA

-- Szerepkörök
INSERT OR IGNORE INTO Roles (Id, Name, DisplayName, Description) VALUES 
    (1, 'admin', 'Adminisztrátor', 'Teljes hozzáféréssel rendelkező adminisztrátor'),
    (2, 'user', 'Felhasználó', 'Bejelentkezett felhasználó'),
    (3, 'guest', 'Vendég', 'Nem bejelentkezett vendég');

-- Jogosultságok
INSERT OR IGNORE INTO Permissions (Id, Name, DisplayName, Category, Description) VALUES 
    -- Termékek
    (1, 'product.view', 'Termékek megtekintése', 'Termék', 'Termékek listázása és részletei'),
    (2, 'product.create', 'Termék létrehozása', 'Termék', 'Új termék felvétele'),
    (3, 'product.edit', 'Termék szerkesztése', 'Termék', 'Termék adatainak módosítása'),
    (4, 'product.delete', 'Termék törlése', 'Termék', 'Termék eltávolítása'),
    
    -- Rendelések
    (5, 'order.view_own', 'Saját rendelés megtekintése', 'Rendelés', 'Saját rendelések listázása'),
    (6, 'order.view_all', 'Minden rendelés megtekintése', 'Rendelés', 'Összes rendelés megtekintése'),
    (7, 'order.create', 'Rendelés létrehozása', 'Rendelés', 'Új rendelés felvétele'),
    (8, 'order.edit', 'Rendelés szerkesztése', 'Rendelés', 'Rendelés módosítása'),
    (9, 'order.delete', 'Rendelés törlése', 'Rendelés', 'Rendelés eltávolítása'),
    (10, 'order.update_status', 'Rendelés státusz módosítása', 'Rendelés', 'Rendelés állapotának változtatása'),
    
    -- Foglalások
    (11, 'reservation.create', 'Foglalás létrehozása', 'Foglalás', 'Új asztalfoglalás'),
    (12, 'reservation.view_own', 'Saját foglalás megtekintése', 'Foglalás', 'Saját foglalások listázása'),
    (13, 'reservation.view_all', 'Minden foglalás megtekintése', 'Foglalás', 'Összes foglalás megtekintése'),
    (14, 'reservation.edit', 'Foglalás szerkesztése', 'Foglalás', 'Foglalás módosítása'),
    (15, 'reservation.delete', 'Foglalás törlése', 'Foglalás', 'Foglalás eltávolítása'),
    (16, 'reservation.manage_all', 'Foglalások teljes kezelése', 'Foglalás', 'Zárt körű napok kezelése, minden foglalás módosítása'),
    
    -- Kosár
    (17, 'cart.view', 'Kosár megtekintése', 'Kosár', 'Kosár tartalmának megtekintése'),
    (18, 'cart.edit', 'Kosár szerkesztése', 'Kosár', 'Kosár tartalmának módosítása'),
    
    -- Profil
    (19, 'profile.view', 'Profil megtekintése', 'Profil', 'Saját profil megtekintése'),
    (20, 'profile.edit', 'Profil szerkesztése', 'Profil', 'Saját profil módosítása');


-- JOGOSULTSÁGOK SZEREPKÖRÖKHÖZ RENDELÉSE

-- Admin jogosultságok (minden)
INSERT OR IGNORE INTO RolePermissions (RoleId, PermissionId)
SELECT 1, Id FROM Permissions;

-- User jogosultságok
INSERT OR IGNORE INTO RolePermissions (RoleId, PermissionId)
SELECT 2, Id FROM Permissions 
WHERE Name IN (
    'product.view',
    'order.view_own', 'order.create',
    'reservation.view_own', 'reservation.create',
    'cart.view', 'cart.edit',
    'profile.view', 'profile.edit'
);

-- Guest jogosultságok
INSERT OR IGNORE INTO RolePermissions (RoleId, PermissionId)
SELECT 3, Id FROM Permissions 
WHERE Name IN (
    'product.view',
    'reservation.create',
    'cart.view', 'cart.edit'
);

-- MEGLÉVŐ FELHASZNÁLÓK SZEREPKÖRÖK BEÁLLÍTÁSA

-- Admin felhasználó (admin szerepkör)
INSERT OR IGNORE INTO UserRoles (UserId, RoleId, AssignedBy)
SELECT u.UserName, r.Id, 'system'
FROM User u, Roles r
WHERE u.UserName = 'admin' AND r.Name = 'admin';

-- Minden más felhasználó (user szerepkör)
INSERT OR IGNORE INTO UserRoles (UserId, RoleId, AssignedBy)
SELECT u.UserName, r.Id, 'system'
FROM User u, Roles r
WHERE u.UserName != 'admin' AND r.Name = 'user';