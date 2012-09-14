﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Samba.Domain.Models.Accounts;
using Samba.Domain.Models.Resources;
using Samba.Infrastructure.Data;
using Samba.Localization.Properties;
using Samba.Persistance.Data;
using Samba.Services.Common;
using Samba.Persistance.Data.Specification;

namespace Samba.Services.Implementations.AccountModule
{
    [Export(typeof(IAccountService))]
    public class AccountService : AbstractService, IAccountService
    {
        private readonly ICacheService _cacheService;

        [ImportingConstructor]
        public AccountService(ICacheService cacheService)
        {
            _cacheService = cacheService;
            ValidatorRegistry.RegisterDeleteValidator(new AccountDeleteValidator());
            ValidatorRegistry.RegisterDeleteValidator(new AccountTemplateDeleteValidator());
            ValidatorRegistry.RegisterDeleteValidator<AccountTransactionTemplate>(x => Dao.Exists<AccountTransactionDocumentTemplate>(y => y.TransactionTemplates.Any(z => z.Id == x.Id)), Resources.AccountTransactionTemplate, Resources.DocumentTemplate);
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<Account>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.Account)));
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<AccountTemplate>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.AccountTemplate)));
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<AccountTransactionTemplate>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.AccountTransactionTemplate)));
            ValidatorRegistry.RegisterSaveValidator(new NonDuplicateSaveValidator<AccountTransactionDocumentTemplate>(string.Format(Resources.SaveErrorDuplicateItemName_f, Resources.DocumentTemplate)));
        }

        private int? _accountCount;
        public int GetAccountCount()
        {
            return (int)(_accountCount ?? (_accountCount = Dao.Count<Resource>()));
        }

        public void CreateNewTransactionDocument(Account selectedAccount, AccountTransactionDocumentTemplate documentTemplate, string description, decimal amount, IEnumerable<Account> accounts)
        {
            using (var w = WorkspaceFactory.Create())
            {
                var document = documentTemplate.CreateDocument(selectedAccount, description, amount, GetExchangeRate(selectedAccount), accounts != null ? accounts.ToList() : null);
                w.Add(document);
                w.CommitChanges();
            }
        }

        public decimal GetAccountBalance(int accountId)
        {
            return Dao.Sum<AccountTransactionValue>(x => x.Debit - x.Credit, x => x.AccountId == accountId);
        }

        public Dictionary<Account, BalanceValue> GetAccountBalances(IList<int> accountTemplateIds, Expression<Func<AccountTransactionValue, bool>> filter)
        {
            using (var w = WorkspaceFactory.CreateReadOnly())
            {
                var accountIds = w.Queryable<Account>().Where(x => accountTemplateIds.Contains(x.AccountTemplateId)).Select(x => x.Id);
                Expression<Func<AccountTransactionValue, bool>> func = x => accountIds.Contains(x.AccountId);
                if (filter != null) func = func.And(filter);
                var transactionValues = w.Queryable<AccountTransactionValue>()
                    .Where(func)
                    .GroupBy(x => x.AccountId)
                    .Select(x => new { Id = x.Key, Amount = x.Sum(y => y.Debit - y.Credit), Exchange = x.Sum(y => y.Exchange) })
                    .ToDictionary(x => x.Id, x => new BalanceValue { Balance = x.Amount, Exchange = x.Exchange });

                return w.Queryable<Account>().Where(x => accountTemplateIds.Contains(x.AccountTemplateId)).ToDictionary(x => x, x => transactionValues.ContainsKey(x.Id) ? transactionValues[x.Id] : BalanceValue.Empty);
            }
        }

        public Dictionary<AccountTemplate, BalanceValue> GetAccountTemplateBalances(IList<int> accountTemplateIds, Expression<Func<AccountTransactionValue, bool>> filter)
        {
            using (var w = WorkspaceFactory.CreateReadOnly())
            {
                Expression<Func<AccountTransactionValue, bool>> func = x => accountTemplateIds.Contains(x.AccountTemplateId);
                if (filter != null) func = func.And(filter);
                var transactionValues = w.Queryable<AccountTransactionValue>()
                    .Where(func)
                    .GroupBy(x => x.AccountTemplateId)
                    .Select(x => new { Id = x.Key, Amount = x.Sum(y => y.Debit - y.Credit), Exchange = x.Sum(y => y.Exchange) })
                    .ToDictionary(x => x.Id, x => new BalanceValue { Balance = x.Amount, Exchange = x.Exchange });
                return w.Queryable<AccountTemplate>().Where(x => accountTemplateIds.Contains(x.Id)).ToDictionary(x => x, x => transactionValues.ContainsKey(x.Id) ? transactionValues[x.Id] : BalanceValue.Empty);
            }
        }

        public string GetCustomData(Account account, string fieldName)
        {
            var cd = Dao.Select<Resource, string>(x => x.CustomData, x => x.AccountId == account.Id).SingleOrDefault();
            return string.IsNullOrEmpty(cd) ? "" : Resource.GetCustomData(cd, fieldName);
        }

        public string GetDescription(AccountTransactionDocumentTemplate documentTemplate, Account account)
        {
            var result = documentTemplate.DescriptionTemplate;
            if (string.IsNullOrEmpty(result)) return result;
            while (Regex.IsMatch(result, "\\[:([^\\]]+)\\]"))
            {
                var match = Regex.Match(result, "\\[:([^\\]]+)\\]");
                result = result.Replace(match.Groups[0].Value, GetCustomData(account, match.Groups[1].Value));
            }
            if (result.Contains("[MONTH]")) result = result.Replace("[MONTH]", DateTime.Now.ToMonthName());
            if (result.Contains("[NEXT MONTH]")) result = result.Replace("[NEXT MONTH]", DateTime.Now.ToNextMonthName());
            if (result.Contains("[WEEK]")) result = result.Replace("[WEEK]", DateTime.Now.WeekOfYear().ToString());
            if (result.Contains("[NEXT WEEK]")) result = result.Replace("[NEXT WEEK]", (DateTime.Now.NextWeekOfYear()).ToString());
            if (result.Contains("[YEAR]")) result = result.Replace("[YEAR]", (DateTime.Now.Year).ToString());
            if (result.Contains("[ACCOUNT NAME]")) result = result.Replace("[ACCOUNT NAME]", account.Name);
            return result;
        }

        public decimal GetDefaultAmount(AccountTransactionDocumentTemplate documentTemplate, Account account)
        {
            decimal result = 0;
            if (!string.IsNullOrEmpty(documentTemplate.DefaultAmount))
            {
                var da = documentTemplate.DefaultAmount;
                if (Regex.IsMatch(da, "\\[:([^\\]]+)\\]"))
                {
                    var match = Regex.Match(da, "\\[:([^\\]]+)\\]");
                    da = GetCustomData(account, match.Groups[1].Value);
                    decimal.TryParse(da, out result);
                }
                else if (da == string.Format("[{0}]", Resources.Balance))
                    result = Math.Abs(GetAccountBalance(account.Id));
                else decimal.TryParse(da, out result);
            }
            return result;
        }

        public string GetAccountNameById(int accountId)
        {
            if (Dao.Exists<Account>(x => x.Id == accountId))
                return Dao.Select<Account, string>(x => x.Name, x => x.Id == accountId).First();
            return "";
        }

        public int GetAccountIdByName(string accountName)
        {
            var acName = accountName.ToLower();
            if (Dao.Exists<Account>(x => x.Name.ToLower() == acName))
                return Dao.Select<Account, int>(x => x.Id, x => x.Name.ToLower() == acName).FirstOrDefault();
            return 0;
        }

        public IEnumerable<Account> GetAccounts(params AccountTemplate[] accountTemplates)
        {
            if (accountTemplates.Count() == 0) return Dao.Query<Account>();
            var ids = accountTemplates.Select(x => x.Id);
            return Dao.Query<Account>(x => ids.Contains(x.AccountTemplateId));
        }

        public IEnumerable<Account> GetAccounts(int accountTemplateId)
        {
            return Dao.Query<Account>(x => x.AccountTemplateId == accountTemplateId);
        }

        public IEnumerable<Account> GetBalancedAccounts(int accountTemplateId)
        {
            using (var w = WorkspaceFactory.CreateReadOnly())
            {
                var q1 = w.Queryable<AccountTransactionValue>().GroupBy(x => x.AccountId).Where(
                        x => x.Sum(y => y.Debit - y.Credit) != 0).Select(x => x.Key);
                return w.Queryable<Account>().Where(x => x.AccountTemplateId == accountTemplateId && q1.Contains(x.Id)).ToList();
            }
        }

        public IEnumerable<string> GetCompletingAccountNames(int accountTemplateId, string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return null;
            var lacn = accountName.ToLower();
            return Dao.Select<Account, string>(x => x.Name, x => x.AccountTemplateId == accountTemplateId && x.Name.ToLower().Contains(lacn));
        }

        public Account GetAccountById(int accountId)
        {
            return Dao.Single<Account>(x => x.Id == accountId);
        }

        public IEnumerable<AccountTemplate> GetAccountTemplates()
        {
            return Dao.Query<AccountTemplate>().OrderBy(x => x.Order);
        }

        public int CreateAccount(string accountName, int accountTemplateId)
        {
            if (accountTemplateId == 0 || string.IsNullOrEmpty(accountName)) return 0;
            if (Dao.Exists<Account>(x => x.Name == accountName)) return 0;
            using (var w = WorkspaceFactory.Create())
            {
                var account = new Account { AccountTemplateId = accountTemplateId, Name = accountName };
                w.Add(account);
                w.CommitChanges();
                return account.Id;
            }
        }

        public decimal GetExchangeRate(Account account)
        {
            if (account.ForeignCurrencyId == 0) return 1;
            return _cacheService.GetForeignCurrencies().Single(x => x.Id == account.ForeignCurrencyId).ExchangeRate;
        }

        public override void Reset()
        {
            _accountCount = null;
        }
    }

    public class AccountTemplateDeleteValidator : SpecificationValidator<AccountTemplate>
    {
        public override string GetErrorMessage(AccountTemplate model)
        {
            if (Dao.Exists<Account>(x => x.AccountTemplateId == model.Id))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.AccountTemplate, Resources.Account);
            if (Dao.Exists<AccountTransactionDocumentTemplate>(x => x.MasterAccountTemplateId == model.Id))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.AccountTemplate, Resources.DocumentTemplate);
            return "";
        }
    }

    public class AccountDeleteValidator : SpecificationValidator<Account>
    {
        public override string GetErrorMessage(Account model)
        {
            if (Dao.Exists<Resource>(x => x.AccountId == model.Id))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.Account, Resources.Resource);
            if (Dao.Exists<AccountTransactionValue>(x => x.AccountId == model.Id))
                return string.Format(Resources.DeleteErrorUsedBy_f, Resources.Account, Resources.AccountTransaction);
            return "";
        }
    }



}
