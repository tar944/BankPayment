﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Adin.BankPayment.Service;
using Adin.BankPayment.Domain.Model;
using Adin.BankPayment.Connector.Enum;
using System.Web;

namespace Adin.BankPayment.Controllers
{
    public class SamanController : Controller
    {
        private string _referenceNumber = string.Empty;
        private string _reservationNumber = string.Empty;
        private string _transactionState = string.Empty;
        private string _traceNumber = string.Empty;
        private bool _isError = false;
        private string _errorMsg = "";
        private string _succeedMsg = "";        
        private string _webBaseUrl = "";        
        private readonly ILogger<SamanController> _logger;
        private IRepository<Transaction> _transactionRepository;
        private IRepository<Application> _applicationRepository;
        private IRepository<Bank> _bankRepository;
        private IRepository<ApplicationBank> _applicationBankRepository;


        public SamanController(ILogger<SamanController> logger,
                               IRepository<Transaction> transactionRepository,
                               IRepository<Application> applicationRepository,
                               IRepository<Bank> bankRepository,
                               IRepository<ApplicationBank> applicationBankRepository)
        {
            _logger = logger;
            _transactionRepository = transactionRepository;
            _applicationRepository = applicationRepository;
            _bankRepository = bankRepository;
            _applicationBankRepository = applicationBankRepository;

        }


        [HttpPost]
        public async Task<IActionResult> Callback(string token, string secondTrackCode)
        {
            _webBaseUrl = string.Format("{0}://{1}", Request.Scheme, Request.Host);
            _logger.LogDebug("CallBack");
            _logger.LogDebug("token:" + token);
            _logger.LogDebug("secondTrackCode:" + secondTrackCode);
            Transaction transaction = await _transactionRepository.Get(Guid.Parse(secondTrackCode));
            if (transaction.Status != (byte)TransactionStatusEnum.Initial)
            {
                return BadRequest();
            }

            string longurl = transaction.CallbackUrl;
            var uriBuilder = new UriBuilder(longurl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);       
            string message = "";
            var refNum = Request.Form["RefNum"];

            BankErrorCodeEnum bankErrorCode = BankErrorCodeEnum.UnkownError;
            try
            {
                if (RequestUnpack())
                {
                    if (_transactionState.Equals("OK"))
                    {


                        bankErrorCode = BankErrorCodeEnum.NoError;
                        transaction.BankErrorCode = (byte)bankErrorCode;
                        transaction.Status = (byte)TransactionStatusEnum.BankOk;
                        transaction.ModifiedOn = DateTime.Now;
                        transaction.ModifiedBy = 1;
                        transaction.BankTrackCode = refNum;
                        await _transactionRepository.Update(transaction);


                        query["id"] = transaction.Id.ToString();
                        query["trackCode"] = transaction.UserTrackCode.ToString();
                        query["status"] = true.ToString();
                        query["errorCode"] = bankErrorCode.ToString();
                        query["message"] = message;
                        uriBuilder.Query = query.ToString();
                        longurl = uriBuilder.ToString();
                        var url3 = string.Format(longurl);
                        return Redirect(url3);                   
                    }
                    else
                    {
                        _isError = true;
                        _errorMsg = "متاسفانه بانک خريد شما را تاييد نکرده است";
                        if (_transactionState.Equals("CanceledByUser") || _transactionState.Equals(string.Empty))
                        {
                            // Transaction was canceled by user
                            _isError = true;
                            _errorMsg = "تراكنش توسط خريدار كنسل شد";
                            bankErrorCode = BankErrorCodeEnum.CanceledByUser;
                        }
                        //InvalidParameters
                        else if (_transactionState.Equals("InvalidParameters"))
                        {
                            // Amount of revers teransaction is more than teransaction
                            _errorMsg = "پارامترهای ارسال شده به بانک اشتباه است";
                            bankErrorCode = BankErrorCodeEnum.InvalidParameters;
                        }
                        else if (_transactionState.Equals("InvalidAmount"))
                        {
                            // Amount of revers teransaction is more than teransaction
                            _errorMsg = "مبلغ تراکنش معکوس بیشتر از مبلغ تراکنش است";
                            bankErrorCode = BankErrorCodeEnum.InvalidAmount;
                        }
                        else if (_transactionState.Equals("InvalidTransaction"))
                        {
                            // Can not find teransaction
                            _errorMsg = "تراکنش معتبر نمی باشد";
                            bankErrorCode = BankErrorCodeEnum.InvalidTransaction;
                        }
                        else if (_transactionState.Equals("InvalidCardNumber"))
                        {
                            // Card number is wrong
                            _errorMsg = "شماره کارت معتبر نمی باشد";
                            bankErrorCode = BankErrorCodeEnum.InvalidCardNumber;
                        }
                        else if (_transactionState.Equals("NoSuchIssuer"))
                        {
                            // Issuer can not find
                            _errorMsg = "صادر کننده پیدا نشد";
                            bankErrorCode = BankErrorCodeEnum.NoSuchIssuer;
                        }
                        else if (_transactionState.Equals("ExpiredCardPickUp"))
                        {
                            // The card is expired
                            _errorMsg = "کارت انتخاب شده منقضی شده است";
                            bankErrorCode = BankErrorCodeEnum.ExpiredCardPickUp;
                        }
                        else if (_transactionState.Equals("AllowablePINTriesExceededPickUp"))
                        {
                            // For third time user enter a wrong PIN so card become invalid
                            _errorMsg = "پین انتخاب شده محدودیت کارت دارد";
                            bankErrorCode = BankErrorCodeEnum.AllowablePINTriesExceededPickUp;
                        }
                        else if (_transactionState.Equals("IncorrectPIN"))
                        {
                            // Pin card is wrong
                            _errorMsg = "پین کد اشتباه است";
                            bankErrorCode = BankErrorCodeEnum.IncorrectPIN;
                        }
                        else if (_transactionState.Equals("ExceedsWithdrawalAmountLimit"))
                        {
                            // Exceeds withdrawal from amount limit
                            _errorMsg = "پرداخت بیشتر از از حد مجاز می باشد";
                            bankErrorCode = BankErrorCodeEnum.ExceedsWithdrawalAmountLimit;
                        }
                        else if (_transactionState.Equals("TransactionCannotBeCompleted"))
                        {
                            // PIN and PAD are currect but Transaction Cannot Be Completed
                            _errorMsg = "تراکنش کامل نشد";
                            bankErrorCode = BankErrorCodeEnum.TransactionCannotBeCompleted;
                        }
                        else if (_transactionState.Equals("ResponseReceivedTooLate"))
                        {
                            // Timeout occur
                            _errorMsg = "جواب کاربر بیشتر از حد مجاز طول کشید";
                            bankErrorCode = BankErrorCodeEnum.ResponseReceivedTooLate;
                        }
                        else if (_transactionState.Equals("SuspectedFraudPickUp"))
                        {
                            // User did not insert cvv2 & expiredate or they are wrong.
                            _errorMsg = "کاربر اطلاعات کارت خود را به درستی وارد نکرده است";
                            bankErrorCode = BankErrorCodeEnum.SuspectedFraudPickUp;
                        }
                        else if (_transactionState.Equals("NoSufficientFunds"))
                        {
                            // there are not suficient funds in the account
                            _errorMsg = "موجودی کافی نمی باشد";
                            bankErrorCode = BankErrorCodeEnum.NoSufficientFunds;
                        }
                        else if (_transactionState.Equals("IssuerDownSlm"))
                        {
                            // The bank server is down
                            _errorMsg = "سرور بانک غیرفعال است";
                            bankErrorCode = BankErrorCodeEnum.BankServerIsDown;
                        }
                        else if (_transactionState.Equals("TMEError"))
                        {
                            // unknown error occur
                            _errorMsg = "خطای ناشناخته";
                            bankErrorCode = BankErrorCodeEnum.UnkownError;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                _errorMsg = ex.Message;
                _logger.LogError(ex.Message);
            }
            _logger.LogError(_errorMsg);         
            var mid1 = Request.Form["MID"];

            transaction.Status = (byte)TransactionStatusEnum.Failed;
            transaction.BankErrorCode = (byte)bankErrorCode;
            transaction.BankErrorMessage = _errorMsg;
            transaction.ModifiedOn = DateTime.Now;
            transaction.ModifiedBy = 1;
            await _transactionRepository.Update(transaction);



            query["id"] = transaction.Id.ToString();
            query["trackCode"] = transaction.UserTrackCode.ToString();
            query["status"] = false.ToString();
            query["errorCode"] = ((byte)bankErrorCode).ToString();
            query["message"] = _errorMsg;
            uriBuilder.Query = query.ToString();
            longurl = uriBuilder.ToString();
            var url = string.Format(longurl);
            return Redirect(url);
        }

        private bool RequestUnpack()
        {
            //    if (RequestFeildIsEmpty()) return false;
            _logger.LogDebug("requestpack");
            _referenceNumber = Request.Form["RefNum"].ToString();
            _logger.LogDebug(_referenceNumber);
            _reservationNumber = Request.Form["ResNum"].ToString();
            _logger.LogDebug(_reservationNumber);
            _transactionState = Request.Form["state"].ToString();
            _logger.LogDebug(_transactionState);
            _traceNumber = Request.Form["TraceNo"].ToString();
            _logger.LogDebug(_traceNumber);

            return true;
        }

        private bool RequestFeildIsEmpty()
        {

            _logger.LogDebug("RequestFeildIsEmpty");
            if (Request.Form["state"].ToString().Equals(string.Empty))
            {
                _logger.LogError("state");
                _isError = true;
                _errorMsg = "خريد شما توسط بانک تاييد شده است اما رسيد ديجيتالي شما تاييد نگشت! مشکلي در فرايند رزرو خريد شما پيش آمده است";
                return true;
            }

            if (Request.Form["RefNum"].ToString().Equals(string.Empty) && Request.Form["state"].ToString().Equals(string.Empty))
            {
                _logger.LogError("RefNum");
                _isError = true;
                _errorMsg = "فرايند انتقال وجه با موفقيت انجام شده است اما فرايند تاييد رسيد ديجيتالي با خطا مواجه گشت";
                return true;
            }

            if (Request.Form["ResNum"].ToString().Equals(string.Empty) && Request.Form["state"].ToString().Equals(string.Empty))
            {
                _logger.LogError("ResNum");
                _isError = true;
                _errorMsg = "خطا در برقرار ارتباط با بانک";
                return true;
            }
            return false;
        }       
      
    }
}
