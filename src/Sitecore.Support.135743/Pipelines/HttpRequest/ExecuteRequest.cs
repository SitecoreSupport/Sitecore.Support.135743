using Microsoft.Extensions.DependencyInjection;
using Sitecore.Abstractions;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.DependencyInjection;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Layouts;
using Sitecore.Security.Accounts;
using Sitecore.SecurityModel;
using Sitecore.Sites;
using Sitecore.Text;
using Sitecore.Web;
using System;
using System.Collections.Generic;
using System.Web;
using Sitecore.Links;
using Sitecore.Pipelines.HttpRequest;

namespace Sitecore.Support.Pipelines.HttpRequest
{
  /// <summary>Executes the request.</summary>
  public class ExecuteRequest : HttpRequestProcessor
  {
    /// <summary>The site manager.</summary>
    private readonly BaseSiteManager _siteManager;
    /// <summary>The item manager.</summary>
    private readonly BaseItemManager _itemManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="T:Sitecore.Pipelines.HttpRequest.ExecuteRequest" /> class.
    /// </summary>
    [Obsolete("Use another constructor overload with dependency injection.")]
    public ExecuteRequest()
      : this(ServiceLocator.ServiceProvider.GetRequiredService<BaseSiteManager>(), ServiceLocator.ServiceProvider.GetRequiredService<BaseItemManager>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="T:Sitecore.Pipelines.HttpRequest.ExecuteRequest" /> class.
    /// </summary>
    /// <param name="siteManager">The site manager.</param>
    /// <param name="itemManager">The item manager.</param>
    public ExecuteRequest(BaseSiteManager siteManager, BaseItemManager itemManager)
    {
      this._siteManager = siteManager;
      this._itemManager = itemManager;
    }

    /// <summary>Gets the item manager. Used to provide layout item.</summary>
    /// <value>The item manager.</value>
    protected virtual BaseItemManager ItemManager
    {
      get
      {
        return this._itemManager;
      }
    }

    /// <summary>
    /// Gets the site manager to check <see cref="M:Sitecore.Abstractions.BaseSiteManager.CanEnter(System.String,Sitecore.Security.Accounts.Account)" /> site access.
    /// </summary>
    /// <value>The site manager.</value>
    protected virtual BaseSiteManager SiteManager
    {
      get
      {
        return this._siteManager;
      }
    }

    /// <summary>Gets the context user.</summary>
    /// <value>The context user.</value>
    protected virtual User ContextUser
    {
      get
      {
        return Context.User;
      }
    }

    /// <summary>Runs the processor.</summary>
    /// <param name="args">The arguments.</param>
    public override void Process(HttpRequestArgs args)
    {
      Assert.ArgumentNotNull((object)args, nameof(args));
      SiteContext site = Context.Site;
      if (site != null && !this.SiteManager.CanEnter(site.Name, (Account)this.ContextUser))
      {
        this.HandleSiteAccessDenied(site, args);
      }
      else
      {
        PageContext page = Context.Page;
        Assert.IsNotNull((object)page, "No page context in processor.");
        string filePath = page.FilePath;
        if (filePath.Length > 0)
        {
          if (WebUtil.IsExternalUrl(filePath))
          {
            args.Context.Response.Redirect(filePath, true);
          }
          else
          {
            if (string.Compare(filePath, HttpContext.Current.Request.Url.LocalPath, StringComparison.InvariantCultureIgnoreCase) == 0)
              return;
            args.Context.RewritePath(filePath, args.Context.Request.PathInfo, args.Url.QueryString, false);
          }
        }
        else if (Context.Item == null)
          this.HandleItemNotFound(args);
        else
          this.HandleLayoutNotFound(args);
      }
    }

    /// <summary>Redirects to login page.</summary>
    /// <param name="url">The URL.</param>
    protected virtual void RedirectToLoginPage(string url)
    {
      UrlString urlString = new UrlString(url);
      if (string.IsNullOrEmpty(urlString["returnUrl"]))
      {
        urlString["returnUrl"] = WebUtil.GetRawUrl();
        urlString.Parameters.Remove("item");
        urlString.Parameters.Remove("user");
        urlString.Parameters.Remove("site");
      }
      WebUtil.Redirect(urlString.ToString(), false);
    }

    /// <summary>Preforms redirect on 'item not found' condition.</summary>
    /// <param name="url">The URL.</param>
    protected virtual void RedirectOnItemNotFound(string url)
    {
      this.PerformRedirect(url);
    }

    /// <summary>Redirects on 'no access' condition.</summary>
    /// <param name="url">The URL.</param>
    protected virtual void RedirectOnNoAccess(string url)
    {
      this.PerformRedirect(url);
    }

    /// <summary>Redirects the on 'site access denied' condition.</summary>
    /// <param name="url">The URL.</param>
    protected virtual void RedirectOnSiteAccessDenied(string url)
    {
      this.PerformRedirect(url);
    }

    /// <summary>Redirects on 'layout not found' condition.</summary>
    /// <param name="url">The URL.</param>
    protected virtual void RedirectOnLayoutNotFound(string url)
    {
      this.PerformRedirect(url);
    }

    /// <summary>Redirects request to the specified URL.</summary>
    /// <param name="url">The URL.</param>
    protected virtual void PerformRedirect(string url)
    {
      if (Settings.RequestErrors.UseServerSideRedirect)
        HttpContext.Current.Server.Transfer(url);
      else
        WebUtil.Redirect(url, false);
    }

    /// <summary>Gets the no access URL.</summary>
    /// <returns>The no access URL.</returns>
    protected virtual string GetNoAccessUrl(out bool loginPage)
    {
      SiteContext site = Context.Site;
      loginPage = false;
      User contextUser = this.ContextUser;
      if (site != null && site.LoginPage.Length > 0)
      {
        if (this.SiteManager.CanEnter(site.Name, (Account)contextUser) && !contextUser.IsAuthenticated)
        {
          Tracer.Info((object)("Redirecting to login page \"" + site.LoginPage + "\"."));
          loginPage = true;
          return GetSiteLoginPage(site);
        }
        Tracer.Info((object)("Redirecting to the 'No Access' page as the current user '" + (object)contextUser + "' does not have sufficient rights to enter the '" + site.Name + "' site."));
        return Settings.NoAccessUrl;
      }
      Tracer.Warning((object)"Redirecting to \"No Access\" page as no login page was found.");
      return Settings.NoAccessUrl;
    }

    #region FIX 135743
    private static string GetSiteLoginPage(SiteContext site)
    {
      string loginItemPath = site.StartPath + site.LoginPage;
      Item loginItem = Context.Database.GetItem(loginItemPath);
      if (loginItem != null)
      {
        var urlOptions = Sitecore.Links.UrlOptions.DefaultOptions;
        urlOptions.LanguageEmbedding = LanguageEmbedding.Always;
        string loginPage = Sitecore.Links.LinkManager.GetItemUrl(loginItem, urlOptions);
        return loginPage ?? site.LoginPage;
      }
      else
      {
        return site.LoginPage;
      }
    }
    #endregion

    /// <summary>Handles the item not found.</summary>
    /// <param name="args">The arguments.</param>
    private void HandleItemNotFound(HttpRequestArgs args)
    {
      string localPath = args.LocalPath;
      string name = Context.User.Name;
      bool flag = false;
      bool loginPage = false;
      string url1 = Settings.ItemNotFoundUrl;
      if (args.PermissionDenied)
      {
        flag = true;
        url1 = this.GetNoAccessUrl(out loginPage);
      }
      SiteContext site = Context.Site;
      string str = site != null ? site.Name : string.Empty;
      List<string> stringList = new List<string>((IEnumerable<string>)new string[6]
      {
        "item",
        localPath,
        "user",
        name,
        "site",
        str
      });
      if (Settings.Authentication.SaveRawUrl)
        stringList.AddRange((IEnumerable<string>)new string[2]
        {
          "url",
          HttpUtility.UrlEncode(Context.RawUrl)
        });
      string url2 = WebUtil.AddQueryString(url1, stringList.ToArray());
      if (!flag)
      {
        Log.Warn(string.Format("Request is redirected to document not found page. Requested url: {0}, User: {1}, Website: {2}", (object)Context.RawUrl, (object)name, (object)str), (object)this);
        this.RedirectOnItemNotFound(url2);
      }
      else
      {
        if (loginPage)
        {
          Log.Warn(string.Format("Request is redirected to login page. Requested url: {0}, User: {1}, Website: {2}", (object)Context.RawUrl, (object)name, (object)str), (object)this);
          this.RedirectToLoginPage(url2);
        }
        Log.Warn(string.Format("Request is redirected to access denied page. Requested url: {0}, User: {1}, Website: {2}", (object)Context.RawUrl, (object)name, (object)str), (object)this);
        this.RedirectOnNoAccess(url2);
      }
    }

    /// <summary>Handles the layout not found.</summary>
    /// <param name="args">The arguments.</param>
    private void HandleLayoutNotFound(HttpRequestArgs args)
    {
      string empty = string.Empty;
      string str1 = string.Empty;
      string url = string.Empty;
      string message = "Request is redirected to no layout page.";
      DeviceItem device = Context.Device;
      if (device != null)
        str1 = device.Name;
      Item obj1 = Context.Item;
      if (obj1 != null)
      {
        message = message + " Item: " + (object)obj1.Uri;
        if (device != null)
        {
          message += string.Format(" Device: {0} ({1})", (object)device.ID, (object)device.InnerItem.Paths.Path);
          empty = obj1.Visualization.GetLayoutID(device).ToString();
          if (empty.Length > 0)
          {
            Database database = Context.Database;
            Assert.IsNotNull((object)database, "No database on processor.");
            Item obj2 = this._itemManager.GetItem(empty, Language.Current, Sitecore.Data.Version.Latest, database, SecurityCheck.Disable);
            if (obj2 != null && !obj2.Access.CanRead())
            {
              SiteContext site = Context.Site;
              string str2 = site != null ? site.Name : string.Empty;
              string noAccessUrl = Settings.NoAccessUrl;
              string[] strArray = new string[8];
              strArray[0] = "item";
              int index1 = 1;
              string str3 = "Layout: " + empty + " (item: " + args.LocalPath + ")";
              strArray[index1] = str3;
              int index2 = 2;
              string str4 = "user";
              strArray[index2] = str4;
              int index3 = 3;
              string userName = Context.GetUserName();
              strArray[index3] = userName;
              int index4 = 4;
              string str5 = "site";
              strArray[index4] = str5;
              int index5 = 5;
              string str6 = str2;
              strArray[index5] = str6;
              int index6 = 6;
              string str7 = "device";
              strArray[index6] = str7;
              int index7 = 7;
              string str8 = str1;
              strArray[index7] = str8;
              url = WebUtil.AddQueryString(noAccessUrl, strArray);
            }
          }
        }
      }
      if (url.Length == 0)
        url = WebUtil.AddQueryString(Settings.LayoutNotFoundUrl, "item", args.LocalPath, "layout", empty, "device", str1);
      Log.Warn(message, (object)this);
      this.RedirectOnLayoutNotFound(url);
    }

    /// <summary>Handles 'site access denied'.</summary>
    private void HandleSiteAccessDenied(SiteContext site, HttpRequestArgs args)
    {
      string url = WebUtil.AddQueryString(Settings.NoAccessUrl, "item", args.LocalPath, "user", Context.GetUserName(), nameof(site), site.Name, "right", "site:enter");
      Log.Warn(string.Format("Request is redirected to access denied page. Requested url: {0}, User: {1}, Website: {2}", (object)Context.RawUrl, (object)Context.GetUserName(), (object)site.Name), (object)this);
      this.RedirectOnSiteAccessDenied(url);
    }
  }
}
