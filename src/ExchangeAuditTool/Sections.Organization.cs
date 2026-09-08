using System;
using System.Collections.Generic;
using System.Text;

namespace ExchangeAuditTool
{
    // "Organization" category: sharing/federation/org-config singletons plus
    // email address policies. Every Get-* below exists on Exchange on-premises
    // AND Exchange Online (read-only); property surfaces differ slightly, so
    // only widely available properties are curated.
    internal static class SectionsOrganization
    {
        public static void Register()
        {
            AuditRegistry.Register(BuildOrgSharingSection());
            AuditRegistry.Register(BuildAddressPoliciesSection());
        }

        // Properties that are collections -> joined with ',' so a ';' CSV never clashes.
        private static readonly List<string> MultiValued = new List<string>(new string[]
        {
            "Domains", "DomainNames",
            "EnabledEmailAddressTemplates", "DisabledEmailAddressTemplates"
        });

        private static string BuildSelectList(List<string> chosen)
        {
            var exprs = new List<string>();
            foreach (string v in chosen)
            {
                if (MultiValued.Contains(v))
                    exprs.Add("@{Name='" + v + "';Expression={($_." + v + " | ForEach-Object { [string]$_ }) -join ','}}");
                else exprs.Add(v);
            }
            if (exprs.Count == 0) exprs.Add("Name");
            return string.Join(", ", exprs.ToArray());
        }

        private static List<string> Collect(AuditSelection sel, params string[] groupKeys)
        {
            return PsScriptHelpers.Collect(sel, groupKeys);
        }

        // ============================================================ 1. ORG SHARING
        private static AuditSection BuildOrgSharingSection()
        {
            var section = new AuditSection(
                "org-sharing",
                "Sharing & org",
                "Sharing & organization export",
                "Audit coexistence essentials (sharing, relationships, federation, org config). One object per run.",
                "shield",
                AuditScope.Both);
            section.Category = "Organization";
            section.DefaultFileName = "OrgSharing.csv";

            var obj = new AuditOptionGroup("object", "Object", GroupMode.SingleChoice); obj.Columns = 1;
            obj.Add(new AuditOption("sharing", "Sharing policies (Get-SharingPolicy)", "sharing", true));
            obj.Add(new AuditOption("relationships", "Organization relationships (Get-OrganizationRelationship)", "relationships", false));
            obj.Add(new AuditOption("federation", "Federation trust (Get-FederationTrust)", "federation", false));
            obj.Add(new AuditOption("fedorgid", "Federated organization ID (Get-FederatedOrganizationIdentifier)", "fedorgid", false));
            obj.Add(new AuditOption("orgconfig", "Organization config essentials (Get-OrganizationConfig)", "orgconfig", false));
            section.AddGroup(obj);

            var sharing = new AuditOptionGroup("sharing", "Sharing policy properties", GroupMode.MultiCheck); sharing.Columns = 2;
            sharing.AddProp("Name", true);
            sharing.AddProp("Enabled", true);
            sharing.AddProp("Default", true);
            sharing.AddProp("Domains", true);
            section.AddGroup(sharing);

            var rel = new AuditOptionGroup("relationships", "Relationship properties", GroupMode.MultiCheck); rel.Columns = 2;
            rel.AddProp("Name", false);
            rel.AddProp("Enabled", false);
            rel.AddProp("DomainNames", false);
            rel.AddProp("FreeBusyAccessEnabled", false);
            rel.AddProp("FreeBusyAccessLevel", false);
            rel.AddProp("MailboxMoveEnabled", false);
            rel.AddProp("DeliveryReportEnabled", false);
            rel.AddProp("TargetApplicationUri", false);
            rel.AddProp("TargetSharingEpr", false);
            rel.AddProp("TargetOwaURL", false);
            section.AddGroup(rel);

            var fed = new AuditOptionGroup("federation", "Federation properties", GroupMode.MultiCheck); fed.Columns = 2;
            fed.AddProp("Name", false);
            fed.AddProp("ApplicationUri", false);
            fed.AddProp("TokenIssuerUri", false);
            fed.AddProp("AccountNamespace", false);
            fed.AddProp("DelegationTrustLink", false);
            section.AddGroup(fed);

            var org = new AuditOptionGroup("orgconfig", "Org config properties", GroupMode.MultiCheck); org.Columns = 2;
            org.AddProp("Name", false);
            org.AddProp("PublicFoldersEnabled", false);
            org.AddProp("ElcProcessingDisabled", false);
            org.AddProp("MailTipsAllTipsEnabled", false);
            org.AddProp("MailTipsExternalRecipientsTipsEnabled", false);
            org.AddProp("ConnectorsEnabled", false);
            org.AddProp("DefaultPublicFolderAgeLimit", false);
            org.AddProp("PublicFolderShowClientControl", false);
            org.AddProp("OAuth2ClientProfileEnabled", false);
            section.AddGroup(org);

            section.BuildScript = delegate (AuditSelection sel, ScriptContext ctx)
            {
                string which = sel.First("object", "sharing");
                string cmdlet;
                string propGroup;
                string label;
                if (which == "relationships") { cmdlet = "Get-OrganizationRelationship"; propGroup = "relationships"; label = "organization relationship(s)"; }
                else if (which == "federation") { cmdlet = "Get-FederationTrust"; propGroup = "federation"; label = "federation trust(s)"; }
                else if (which == "fedorgid") { cmdlet = "Get-FederatedOrganizationIdentifier"; propGroup = "federation"; label = "federated organization ID(s)"; }
                else if (which == "orgconfig") { cmdlet = "Get-OrganizationConfig"; propGroup = "orgconfig"; label = "organization config(s)"; }
                else { cmdlet = "Get-SharingPolicy"; propGroup = "sharing"; label = "sharing policies"; }

                var chosen = Collect(sel, propGroup);
                if (chosen.Count == 0) chosen.Add("Name");
                string selectList = BuildSelectList(chosen);

                var sb = new StringBuilder();
                sb.AppendLine("Write-Host 'Querying " + label + "...'");
                sb.AppendLine("$items = @(" + cmdlet + " -ErrorAction SilentlyContinue)");
                sb.AppendLine("Write-Host (\"Retrieved {0} " + label + ".\" -f $items.Count)");
                sb.AppendLine("$rows = $items | Select-Object " + selectList);
                sb.AppendLine();
                sb.Append(ctx.ExportCsv("$rows"));
                sb.AppendLine("Write-Host 'Export complete.'");
                return sb.ToString();
            };

            return section;
        }

        // ============================================================ 2. ADDRESS POLICIES
        private static AuditSection BuildAddressPoliciesSection()
        {
            var section = new AuditSection(
                "address-policies",
                "Address policies",
                "Address policy export",
                "Audit email address policies (Get-EmailAddressPolicy): filters and templates must exist in the target.",
                "rule",
                AuditScope.Both);
            section.Category = "Domains / Routing";
            section.DefaultFileName = "AddressPolicies.csv";

            var identity = new AuditOptionGroup("identity", "Identity", GroupMode.MultiCheck); identity.Columns = 2;
            identity.AddProp("Name", true);
            identity.AddProp("Priority", true);
            identity.AddProp("Enabled", true);
            section.AddGroup(identity);

            var filter = new AuditOptionGroup("filter", "Recipient filter", GroupMode.MultiCheck); filter.Columns = 2;
            filter.Hint = "RecipientFilter recalculates membership live. Do NOT reuse LdapRecipientFilter across environments.";
            filter.AddProp("RecipientFilterType", false);
            filter.AddProp("RecipientContainer", false);
            filter.AddProp("IncludedRecipients", false);
            filter.AddProp("ConditionalCompany", false);
            filter.AddProp("ConditionalDepartment", false);
            filter.AddProp("ConditionalStateOrProvince", false);
            filter.AddProp("ConditionalCustomAttribute1", false);
            filter.AddProp("ConditionalCustomAttribute2", false);
            filter.AddProp("ConditionalCustomAttribute3", false);
            filter.AddProp("ConditionalCustomAttribute4", false);
            filter.AddProp("ConditionalCustomAttribute5", false);
            filter.AddProp("LdapRecipientFilter", false);
            section.AddGroup(filter);

            var templates = new AuditOptionGroup("templates", "Address templates", GroupMode.MultiCheck); templates.Columns = 1;
            templates.AddProp("EnabledEmailAddressTemplates", true);
            templates.AddProp("DisabledEmailAddressTemplates", false);
            templates.AddProp("EnabledPrimarySMTPAddressTemplate", false);
            section.AddGroup(templates);

            section.BuildScript = delegate (AuditSelection sel, ScriptContext ctx)
            {
                var chosen = Collect(sel, "identity", "filter", "templates");
                if (chosen.Count == 0) chosen.Add("Name");
                string selectList = BuildSelectList(chosen);

                var sb = new StringBuilder();
                sb.AppendLine("Write-Host 'Querying email address policies...'");
                sb.AppendLine("$items = @(Get-EmailAddressPolicy -ErrorAction SilentlyContinue)");
                sb.AppendLine("Write-Host (\"Retrieved {0} address policies.\" -f $items.Count)");
                sb.AppendLine("$rows = $items | Select-Object " + selectList);
                sb.AppendLine();
                sb.Append(ctx.ExportCsv("$rows"));
                sb.AppendLine("Write-Host 'Export complete.'");
                return sb.ToString();
            };

            return section;
        }
    }
}
