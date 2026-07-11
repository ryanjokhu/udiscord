using System;
using System.Collections.Generic;
using System.Linq;
using UDiscord.Core.Models;
using UDiscord.Rocket.Configuration;

namespace UDiscord.Rocket.Discord
{
    public sealed class DiscordPermissionService
    {
        private const ulong AdministratorPermission = 1UL << 3;
        private readonly HashSet<ulong> _viewerRoles;
        private readonly HashSet<ulong> _moderatorRoles;
        private readonly HashSet<ulong> _administratorRoles;
        private readonly bool _allowAdministratorBypass;

        public DiscordPermissionService(PermissionSettings settings, bool allowAdministratorBypass)
        {
            settings = settings ?? PermissionSettings.CreateDefault();
            _viewerRoles = new HashSet<ulong>(settings.ViewerRoleIds ?? Enumerable.Empty<ulong>());
            _moderatorRoles = new HashSet<ulong>(settings.ModeratorRoleIds ?? Enumerable.Empty<ulong>());
            _administratorRoles = new HashSet<ulong>(settings.AdministratorRoleIds ?? Enumerable.Empty<ulong>());
            _allowAdministratorBypass = allowAdministratorBypass;
        }

        public PermissionTier GetTier(DiscordInteraction interaction)
        {
            if (interaction == null) return PermissionTier.None;
            if (_allowAdministratorBypass && (interaction.MemberPermissions & AdministratorPermission) == AdministratorPermission)
                return PermissionTier.Administrator;

            IReadOnlyList<ulong> roles = interaction.RoleIds ?? new List<ulong>();
            if (roles.Any(role => _administratorRoles.Contains(role))) return PermissionTier.Administrator;
            if (roles.Any(role => _moderatorRoles.Contains(role))) return PermissionTier.Moderator;
            if (roles.Any(role => _viewerRoles.Contains(role))) return PermissionTier.Viewer;
            return PermissionTier.None;
        }

        public bool IsAllowed(DiscordInteraction interaction, PermissionTier required)
        {
            return GetTier(interaction) >= required;
        }
    }
}
