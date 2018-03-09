<%@ Page Language="C#" AutoEventWireup="true" CodeFile="Test.aspx.cs" Inherits="MyAPP_Test" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
</head>
<body>
    <form id="form1" runat="server">
        <div>

            <h1>
                <center>Rate Plan Comparison Tool</center>
            </h1>
            <asp:Table runat="server" CellPadding="5" HorizontalAlign="Center">
                <asp:TableRow>
                    <asp:TableCell>
                        <asp:Button ID="Button1" runat="server" Text="<<" OnClick="Button1_Click" />
                    </asp:TableCell>
                    <asp:TableCell>
                        <asp:Label ID="Label1" runat="server" Text="Label"></asp:Label>
                    </asp:TableCell>
                    <asp:TableCell>
                        <asp:Button ID="Button2" runat="server" Text=">>" OnClick="Button2_Click" />
                    </asp:TableCell>
                </asp:TableRow>
            </asp:Table>
            <asp:PlaceHolder ID="PlaceHolder1" runat="server"></asp:PlaceHolder>
            <center><asp:Label ID="Label2" runat="server" Text="Label"></asp:Label></center>
        </div>
    </form>
</body>
</html>
