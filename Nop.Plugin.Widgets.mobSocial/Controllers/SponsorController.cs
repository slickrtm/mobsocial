﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Mob.Core;
using Nop.Admin.Models.Orders;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Plugin.Widgets.MobSocial.Domain;
using Nop.Plugin.Widgets.MobSocial.Enums;
using Nop.Plugin.Widgets.MobSocial.Extensions;
using Nop.Plugin.Widgets.MobSocial.Models;
using Nop.Plugin.Widgets.MobSocial.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Web.Controllers;
using Nop.Web.Models.Order;

namespace Nop.Plugin.Widgets.MobSocial.Controllers
{
    public class SponsorController : BasePublicController
    {
        #region Fields

        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly ISponsorService _sponsorService;
        private readonly ISponsorPassService _sponsorPassService;
        private readonly IOrderService _orderService;
        private readonly IVideoBattleService _videoBattleService;
        private readonly ICustomerService _customerService;
        private readonly IPictureService _pictureService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ILocalizationService _localizationService;
        private readonly ICurrencyService _currencyService;
        private readonly IMobSocialMessageService _mobSocialMessageService;
        private readonly MediaSettings _mediaSettings;

        #endregion

        #region ctor
        public SponsorController(IWorkContext workContext,
            ISponsorService sponsorService,
            ISponsorPassService sponsorPassService,
            IOrderService orderService,
            IVideoBattleService videoBattleService,
            ICustomerService customerService,
            IPictureService pictureService,
            IPriceFormatter priceFormatter,
            IDateTimeHelper dateTimeHelper,
            ILocalizationService localizationService,
            ICurrencyService currencyService,
            MediaSettings mediaSettings, IMobSocialMessageService mobSocialMessageService, IStoreContext storeContext)
        {
            _workContext = workContext;
            _sponsorPassService = sponsorPassService;
            _sponsorService = sponsorService;
            _orderService = orderService;
            _videoBattleService = videoBattleService;
            _customerService = customerService;
            _pictureService = pictureService;
            _priceFormatter = priceFormatter;
            _dateTimeHelper = dateTimeHelper;
            _localizationService = localizationService;
            _currencyService = currencyService;
            _mediaSettings = mediaSettings;
            _mobSocialMessageService = mobSocialMessageService;
            _storeContext = storeContext;
        }
        #endregion



        #region Methods

        [HttpPost]
        [Authorize]
        public ActionResult SaveSponsor(SponsorModel Model)
        {
            if (!ModelState.IsValid)
                return Json(new { Success = false, Message = "Invalid Data" });
            
            var battle = _videoBattleService.GetById(Model.BattleId); //todo: query picture battles when ready
            if(battle == null)
                return Json(new { Success = false, Message = "Invalid Battle" });

            //because somebody wants to be a sponsor, let's first query the sponsorship pass
            var sponsorPass = _sponsorPassService.GetSponsorPassByOrderId(Model.SponsorPassId);

            //the sponsor pass must belong to the person in question && it shouldn't have been used
            if (sponsorPass == null || sponsorPass.CustomerId != _workContext.CurrentCustomer.Id || sponsorPass.Status == PassStatus.Used)
                return Json(new { Success = false, Message = "Unauthorized" });

            //lets find the associated order
            var order = _orderService.GetOrderById(sponsorPass.SponsorPassOrderId);

            //there is a possibility that a sponsor is increasing the sponsorship amount. In that case, the new order status will automatically
            //be of same status as that of previous one for the same battle
            //lets find if the sponsor already exist for this battle?
            var existingSponsors = _sponsorService.GetSponsors(order.CustomerId, Model.BattleId, Model.BattleType, null);

            var newSponsorStatus = SponsorshipStatus.Pending;

            //so is the current customer already an sponsor for this battle?
            if (existingSponsors.Any())
                newSponsorStatus = existingSponsors.First().SponsorshipStatus;


            //create the sponsor and set the status to pending or an existing status as determined above. (the status will be marked accepted or rejected or cancelled by battle owner)
            var sponsor = new Sponsor() {
                BattleId = Model.BattleId,
                BattleType = Model.BattleType,
                SponsorshipAmount = order.OrderTotal,
                CustomerId = sponsorPass.CustomerId,
                SponsorshipStatus = newSponsorStatus
            };
            //save the sponsor
            _sponsorService.Insert(sponsor);

            //and mark the sponsor pass used by this battle
            _sponsorPassService.MarkSponsorPassUsed(sponsorPass.SponsorPassOrderId, Model.BattleId, Model.BattleType);


            var battleOwner = _customerService.GetCustomerById(battle.ChallengerId);

            var sponsorCustomer = _customerService.GetCustomerById(sponsorPass.CustomerId);

            //send notification to battle owner
            _mobSocialMessageService.SendSponsorAppliedNotificationToBattleOwner(battleOwner, sponsorCustomer, battle,
                _workContext.WorkingLanguage.Id, _storeContext.CurrentStore.Id);

            return Json(new { Success = true });
        }

        [HttpPost]
        [Authorize]
        public ActionResult UpdateSponsor(UpdateSponsorModel Model)
        {
            //here we update or reject the status of battle
            if (!ModelState.IsValid)
                return Json(new { Success = false, Message = "Invalid Data" });

            //because a sponsor may have performed multiple transactions for a battle, we'd update all of them at once 

            //first get the battle
            //TODO: Remove comment when picture battles are ready
            var battle = _videoBattleService.GetById(Model.BattleId); // Model.BattleType == BattleType.Video ? _videoBattleService.GetById(Model.BattleId) : null;

            //only battle owner should be able to accept or reject the sponsorship
            if (battle.ChallengerId != _workContext.CurrentCustomer.Id && Model.SponsorshipStatus != SponsorshipStatus.Cancelled)
                return Json(new { Success = false, Message = "Unauthorized" });

            //similarly only the sponsor should be able to withdraw it
            if (Model.SponsorCustomerId != _workContext.CurrentCustomer.Id && Model.SponsorshipStatus == SponsorshipStatus.Cancelled)
                return Json(new { Success = false, Message = "Unauthorized" });

            //so lets update all the sponsors for this battle and this user
            _sponsorService.UpdateSponsorStatus(Model.SponsorCustomerId, Model.BattleId, Model.BattleType, Model.SponsorshipStatus);

            Customer customer;
            //send sponsorship update status to battle owner and admin
            if (Model.SponsorshipStatus == SponsorshipStatus.Cancelled)
            {
                customer = _customerService.GetCustomerById(battle.ChallengerId);
                //send this messge to battle owner
            }
            else
            {
                //send this message to sponsor
                customer = _customerService.GetCustomerById(Model.SponsorCustomerId);
            }

            //send notification
            _mobSocialMessageService.SendSponsorshipStatusChangeNotification(customer, Model.SponsorshipStatus,
                    battle, _workContext.WorkingLanguage.Id, _storeContext.CurrentStore.Id);
            return Json(new { Success = true });

        }

        [HttpPost]
        public ActionResult SaveSponsorData(SponsorDataModel Model)
        {
            //here we update or reject the status of battle
            if (!ModelState.IsValid)
                return Json(new { Success = false, Message = "Invalid Data" });

            //because a sponsor may have performed multiple transactions for a battle, we'd update all of them at once 

            //first get the battle
            //TODO: Remove comment when picture battles are ready
            var battle = _videoBattleService.GetById(Model.BattleId); // Model.BattleType == BattleType.Video ? _videoBattleService.GetById(Model.BattleId) : null;

            //first get battle
            var videoBattle = _videoBattleService.GetById(Model.BattleId);
            var sponsors = _sponsorService.GetSponsors(_workContext.CurrentCustomer.Id, Model.BattleId, BattleType.Video,
                SponsorshipStatus.Accepted);
            //only the battle owner or the sponsor can save the sponsor data
            if (!(sponsors.Any() || videoBattle.ChallengerId == _workContext.CurrentCustomer.Id))
                return Json(new { Success = false, Message = "Unauthorized" });

            //get the sponsor data. Because battle owner can also save the data, we need to make sure that correct data is requested
            //because battle owner can save anybody's data while sponsor can save only his or her data
            var sponsorData = _sponsorService.GetSponsorData(Model.BattleId, Model.BattleType, sponsors.Any() ? _workContext.CurrentCustomer.Id : Model.SponsorCustomerId);
            sponsorData.DisplayName = Model.DisplayName;
            sponsorData.PictureId = Model.PictureId;
            sponsorData.TargetUrl = Model.TargetUrl;
            sponsorData.DateUpdated = DateTime.UtcNow;
            
            //display order can only be changed by battle owner depending on the amount or his choice
            if(videoBattle.ChallengerId == _workContext.CurrentCustomer.Id)
                sponsorData.DisplayOrder = Model.DisplayOrder;

            _sponsorService.SaveSponsorData(sponsorData);

            return Json(new {Success = true});

        }

        [HttpPost]
        [Authorize]
        public ActionResult GetSponsors(SponsorsRequestModel Model)
        {
            //first we check if it's the battle owner or a sponsor calling this method?
            //for that we need to query the battle first
            //todo: get picture battle when ready
            var battle = Model.BattleType == BattleType.Video ? _videoBattleService.GetById(Model.BattleId) : null;

            if (battle == null)
                return Json(new { Success = false, Message = "Battle doesn't exist" });

            //lets query the sponsor
            var sponsors = battle.ChallengerId == _workContext.CurrentCustomer.Id
                ? _sponsorService.GetSponsorsGrouped(null, Model.BattleId, Model.BattleType, Model.SponsorshipStatus) //battle owner
                : _sponsorService.GetSponsorsGrouped(_workContext.CurrentCustomer.Id, Model.BattleId, Model.BattleType, Model.SponsorshipStatus); //sponsor or somebody else?

            //to list
            var model = sponsors.Select(s => s.ToPublicModel(_workContext, _customerService, _pictureService, _sponsorService, _priceFormatter, _mediaSettings)).OrderBy(x => x.SponsorData.DisplayOrder).ToList();

            return Json(new {
                Success = true,
                Sponsors = model,
                IsChallenger = battle.ChallengerId == _workContext.CurrentCustomer.Id,
                IsSponsor = model.Any(x => x.CustomerId == _workContext.CurrentCustomer.Id),
                BattleName = battle.Name,
                BattleUrl = battle.GetSeName(_workContext.WorkingLanguage.Id)
            });
        }

        [HttpPost]
        [Authorize]
        public ActionResult GetSponsorTransactions(SponsorTransactionRequestModel Model)
        {
            //only battle owner or sponsor should be able to see transactions
            //todo: get picture battle when ready
            var battle = Model.BattleType == BattleType.Video ? _videoBattleService.GetById(Model.BattleId) : null;
            if (battle == null)
                return Json(new { Success = false, Message = "Battle doesn't exist" });


            if (battle.ChallengerId != _workContext.CurrentCustomer.Id &&
                Model.CustomerId != _workContext.CurrentCustomer.Id)
                return Json(new { Success = false, Message = "Unauthorized" });

            var orders = _sponsorPassService.GetSponsorPassOrders(Model.CustomerId, Model.BattleId, Model.BattleType);
            var model = new List<SponsorTransactionModel>();
            foreach (var order in orders)
            {
                var orderTotalInCustomerCurrency = _currencyService.ConvertCurrency(order.OrderTotal, order.CurrencyRate);
                model.Add(new SponsorTransactionModel() {
                    OrderId = order.Id,
                    TransactionDate = _dateTimeHelper.ConvertToUserTime(order.CreatedOnUtc, DateTimeKind.Utc).ToString(),
                    TransactionAmount = order.OrderTotal,
                    TransactionAmountFormatted = _priceFormatter.FormatPrice(orderTotalInCustomerCurrency, true, order.CustomerCurrencyCode, false, _workContext.WorkingLanguage)
                });
            }

            return Json(new { Success = true, Orders = model });


        }
        public ActionResult SponsorDashboard(BattleType BattleType, int BattleId)
        {
            var battle = _videoBattleService.GetById(BattleId);
            if(battle == null)
                return InvokeHttp404();
            //only battle owner or sponsor can vew dashboard
            var sponsors = _sponsorService.GetSponsors(_workContext.CurrentCustomer.Id, BattleId, BattleType, null);
            if (!sponsors.Any() && battle.ChallengerId != _workContext.CurrentCustomer.Id)
                return InvokeHttp404();

            //TODO: Include picture battles when ready
            var model = new SponsorsRequestModel() {
                BattleType = BattleType,
                BattleId = BattleId,
                SponsorshipStatus = SponsorshipStatus.Pending,
                BattleName = battle.Name,
                BattleUrl = Url.RouteUrl("VideoBattlePage", new {SeName = battle.GetSeName(_workContext.WorkingLanguage.Id, true, false)})
            };
            return View("mobSocial/Sponsor/SponsorDashboard", model);
        }


        [HttpPost]
        public ActionResult UploadPicture(int BattleId, BattleType BattleType, IEnumerable<HttpPostedFileBase> file)
        {

            //first get battle
            var videoBattle = _videoBattleService.GetById(BattleId);
            var sponsors = _sponsorService.GetSponsors(_workContext.CurrentCustomer.Id, BattleId, BattleType,
                SponsorshipStatus.Accepted);
            //only the battle owner or the sponsor can upload the picture
            if (!(sponsors.Any() || videoBattle.ChallengerId == _workContext.CurrentCustomer.Id))
                return Json(new { Success = false, Message = "Unauthorized" });

            var files = file.ToList();
            var newImages = new List<object>();
            foreach (var fi in files)
            {
                Stream stream = null;
                var fileName = "";
                var contentType = "";

                if (file == null)
                    throw new ArgumentException("No file uploaded");

                stream = fi.InputStream;
                fileName = Path.GetFileName(fi.FileName);
                contentType = fi.ContentType;

                var fileBinary = new byte[stream.Length];
                stream.Read(fileBinary, 0, fileBinary.Length);

                var fileExtension = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(fileExtension))
                    fileExtension = fileExtension.ToLowerInvariant();


                if (string.IsNullOrEmpty(contentType))
                {
                    contentType = PictureUtility.GetContentType(fileExtension);
                }

                var picture = _pictureService.InsertPicture(fileBinary, contentType, null);


                newImages.Add(new {
                    ImageUrl = _pictureService.GetPictureUrl(picture.Id),
                    ImageId = picture.Id
                });
            }

            return Json(new { Success = true, Images = newImages });
        }


        #endregion
    }
}