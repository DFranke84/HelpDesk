# HelpDesk Application - Functionality Overview

## User Authentication & Access Control
- **Provides access using the User table**
  - Access can be granted via individual User/password authentication
  - Access can be granted via Windows groups (Active Directory integration)
  - Access can be granted via built-in Windows authentication
  - Provides auto login using Windows groups via Negotiate authentication
  - Provides auto login for individual users associated to domain IDs (SAM account names/UPN)
  - Supports mixed authentication modes (cookie-based and Windows integrated)
  - Implements session management with sliding expiration (~60 minutes)
  - Forces password changes for users flagged with MustChangePassword
  - Provides password reset functionality via email tokens
  - Implements login rate limiting with configurable attempts and time windows

- **User Role Management**
  - Supports Admin and standard user roles
  - Admin users bypass all permission checks
  - Standard users subject to granular page-level permissions
  - Users can be associated with multiple desk groups for access control

- **Page-level CRUD Permissions**
  - Individual users can have Create/Read/Update/Delete permissions per page
  - Group-based users inherit permissions from Windows group mappings
  - Effective permissions are union of direct user permissions and group permissions
  - Permission checks enforced at API and navigation levels

## Application Catalog & Navigation
- **Dynamic Application Catalog**
  - Applications can be enabled/disabled and ordered for display
  - Applications can use internal database or external database connections
  - Applications support both internal Blazor routes and external URLs
  - External URLs can open in new tabs or embedded iframes
  - Page types include regular pages, gradient separators, and shaded separators

- **Menu & Navigation System**
  - Group-based access control determines visible applications and pages
  - Navigation guards prevent unauthorized route access
  - Support for nested routing within applications
  - Dynamic menu generation based on user permissions and group memberships

## Database Management & Architecture
- **Multi-Database Support**
  - Primary HelpDesk database for core functionality
  - Support for external application-specific databases via connection string keys
  - Books module demonstrates separate database integration pattern
  - Database factory pattern for proper context management in Blazor

- **Entity Framework Core Integration**
  - Automatic database migrations on startup (configurable)
  - Design-time database context factories for development
  - Change tracking and audit history for all major entities
  - UTC datetime handling and concurrency control with row versioning

- **Data Seeding & Initialization**
  - Automatic admin user creation if no users exist
  - Configurable database initialization process
  - Sample data seeding for development environments
  - Books catalog integration and sample data setup

## Logging & Monitoring
- **Multi-Target Logging System**
  - File-based logging with rolling file configuration
  - Database logging via stored procedures
  - Email alerting for critical errors via SMTP
  - Structured logging with correlation IDs and user context
  - Activity categorization and elapsed time tracking

- **Admin Log Viewer**
  - Web-based NLog entry viewer with filtering capabilities
  - Search by correlation ID, log levels, and activity categories
  - Maximum 500 entries per query for performance
  - Real-time log monitoring through admin interface

- **Audit & Change History**
  - Automatic change tracking for all major entities
  - JSON-based change storage with before/after values
  - Sensitive field exclusion (passwords, tokens) from audit logs
  - Admin interface for viewing entity change history
  - User and timestamp tracking for all changes

## Email & Communication
- **SMTP Configuration Support**
  - Direct SMTP configuration for simple setups
  - Standard SMTP configuration with authentication
  - Profile-based SMTP switching between Direct and Standard modes
  - MailKit integration for robust email delivery
  - Rate limiting for email alerts to prevent spam

- **Email-Based Features**
  - Password reset tokens sent via email with expiration
  - Account notifications and alerts
  - System alert emails for critical errors
  - Configurable email templates and formatting

## API & Integration Layer
- **RESTful API Endpoints**
  - Account management APIs (login, logout, password changes)
  - Grid data APIs for admin interfaces using DataTables format
  - Diagnostic endpoints for development troubleshooting
  - Rate limiting and security headers for API protection
  - JSON and form-based authentication endpoints

- **Cross-Origin & Security Features**
  - Required fetch headers for API security
  - CSRF protection for form submissions
  - Secure cookie configuration
  - AllowedHosts configuration for deployment security

## User Interface & Experience
- **Blazor Server Architecture**
  - Interactive server-side rendering for responsive UI
  - Real-time updates without page refreshes
  - Error boundaries for graceful error handling
  - SignalR integration for server-client communication

- **Admin Interface**
  - DataTables-based admin grids for all major entities
  - CRUD operations for users, applications, pages, groups
  - Permission matrix management interface
  - Bulk operations support for administrative tasks

- **Responsive Design**
  - Bootstrap-based styling and components
  - jQuery and DataTables integration for enhanced interactions
  - Mobile-friendly responsive layouts
  - Accessibility features and proper ARIA labeling

## Modular Architecture & Extensibility
- **Plugin-Based Module System**
  - Books module demonstrates extension pattern
  - Embedded static assets in modules (CSS, JS, images)
  - Module-specific database contexts and migrations
  - Automatic module discovery and route registration

- **PowerShell Automation**
  - New-HelpDeskModule.ps1 script for creating new modules
  - Automatic project scaffolding with proper structure
  - Solution file updates and project references
  - Build automation and dependency management

## Configuration Management
- **Environment-Specific Settings**
  - Development vs Production configuration profiles
  - Connection string management for multiple databases
  - Feature flags for optional functionality (DB initialization, detailed errors)
  - SMTP profile switching and email configuration

- **Windows Integration Settings**
  - Active Directory group mapping configuration
  - Windows group access level ranking system
  - Group membership caching with configurable timeouts
  - Domain filtering for local vs domain groups

## Security & Compliance
- **Password Security**
  - Hashed password storage (no plaintext)
  - Password policy enforcement
  - Forced password changes on first login
  - Secure password reset token generation and expiration

- **Session Security**
  - Secure cookie configuration with HttpOnly and Secure flags
  - Session timeout and idle detection
  - Automatic logout for inactive sessions
  - Anti-forgery token protection

## Development & Debugging Support
- **Development Tools**
  - Client-side diagnostic logging for Blazor errors
  - Detailed error pages in development mode
  - Database seeding and test data generation
  - Hot reload support for rapid development

- **Deployment Support**
  - IIS in-process hosting configuration
  - Connection string externalization
  - Environment-specific logging configuration
  - Production-ready security headers and policies

## External System Integration
- **Active Directory Integration**
  - Windows group membership resolution
  - User principal name and SAM account mapping
  - Token group enumeration for group memberships
  - Configurable group access ranking for permission inheritance

- **Database Integration Patterns**
  - External database connection string resolution
  - Factory pattern for multiple database contexts
  - Migration support for external databases
  - Connection pooling and resource management